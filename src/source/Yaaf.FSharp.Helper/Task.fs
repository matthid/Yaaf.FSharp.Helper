// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.Helper

//open Yaaf.Logging
open Yaaf.FSharp.Control
open FSharpx
open System
open System.Linq
open System.Threading

[<AllowNullLiteralAttribute>]
type ICallContext =
    abstract member LogicalSetData : string * obj -> unit
    abstract member LogicalGetData : string -> obj

type MyUnit = MyUnit
type WorkerThread () as x =
    do
        WorkerThread.CheckCallContext()
    static let mutable callContext : ICallContext = null
#if FX_NO_CONTEXT
#else
    static do
        callContext <- WorkerThread.DefaultCallContext
    static member DefaultCallContext =
        { new ICallContext with
            member __.LogicalGetData key =
                System.Runtime.Remoting.Messaging.CallContext.LogicalGetData key
            member __.LogicalSetData (key, value) =
                System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(key, value)
        }
#endif
    static member CallContext with get () : ICallContext = callContext and set v = callContext <- v
    [<ThreadStatic>]
    [<DefaultValue>]
    static val mutable private cuWorkerThread : WorkerThread option
    let work = new System.Collections.Concurrent.BlockingCollection<_>()
    let mutable finish = false
    //let waitHandle = new System.Threading.AutoResetEvent(false);
    let workerThread =
        Tasks.Task.Run(fun () ->
            WorkerThread.cuWorkerThread <- Some x
            WorkerThread.CallContext.LogicalSetData("WorkerThread", x)
            while not finish do
                let (cont,econt,ccont) = work.Take()
                try
                    cont()
                with e ->
                    econt e
                ())
    //new () = new WorkerThread(10)
    member x.Dispose () = finish <- true
    member private x.IsFinished = finish
    interface System.IDisposable with
        member x.Dispose() = x.Dispose()
    member x.SwitchToWorker () =
        WorkerThread.CheckCallContext()
        if finish then raise <| System.ObjectDisposedException("Worker already disposed!", null :> exn)
        Async.FromContinuations(fun (cont, econt, ccont) -> 
            work.Add((cont, econt, ccont)))
        //async.Return ()
    member x.SetWorker() = 
        WorkerThread.CheckCallContext()
        WorkerThread.CallContext.LogicalSetData("WorkerThread", x)
    static member CheckCallContext() =
        if WorkerThread.CallContext = null then
            failwith "Please set WorkerThread.CallContext"
    static member WithWorker a =
        WorkerThread.CheckCallContext()
        async {
            use worker = new WorkerThread()
            worker.SetWorker()
            let! res = a
            worker.Dispose()
            return res
        }
    static member WithWorker f =
        WorkerThread.CheckCallContext()
        use worker = new WorkerThread()
        worker.SetWorker()
        let res = f ()
        worker.Dispose()
        res
    static member IsWorkerThread 
        with get () =
            WorkerThread.CheckCallContext()
            WorkerThread.cuWorkerThread.IsSome
    static member SwitchToLogicalWorker(canFail:bool) = 
        WorkerThread.CheckCallContext()
        async {
            //return ()
            match WorkerThread.CallContext.LogicalGetData("WorkerThread") with
            | :? WorkerThread as worker ->
                if not worker.IsFinished then
                    return! worker.SwitchToWorker()
                else
                    if canFail then invalidOp "Worker is already disposed!"
                    //Log.Err (fun () -> "Found a worker but it is already disposed!")
                    return! Async.SwitchToNewThread()
            | _ -> 
                if canFail then invalidOp "Worker value was not set!"
                //Log.Err (fun () -> "Worker value was not set!")
                return! Async.SwitchToNewThread()
        }
    static member SwitchToLogicalWorker() = 
        WorkerThread.CheckCallContext()
        WorkerThread.SwitchToLogicalWorker(false)

// Represents a stream of IObserver events. http://msdn.microsoft.com/en-us/library/ee370313.aspx
type ObservableSource<'T>() =
    let protect function1 =
        let mutable ok = false 
        try 
            function1()
            ok <- true 
        finally
            if not ok then failwith "IObserver method threw an exception."

    let mutable key = 0

    // Use a Map, not a Dictionary, because callers might unsubscribe in the OnNext 
    // method, so thread-safe snapshots of subscribers to iterate over are needed. 
    let mutable subscriptions = Map.empty : Map<int, IObserver<'T>>

    let next(obs) = 
        subscriptions |> Seq.iter (fun (KeyValue(_, value)) -> 
            protect (fun () -> value.OnNext(obs)))

    let completed() = 
        subscriptions |> Seq.iter (fun (KeyValue(_, value)) -> 
            protect (fun () -> value.OnCompleted()))

    let error(err) = 
        subscriptions |> Seq.iter (fun (KeyValue(_, value)) -> 
            protect (fun () -> value.OnError(err)))

    let thisLock = new obj()

    let obs = 
        { new IObservable<'T> with 
            member this.Subscribe(obs) =
                let key1 =
                    lock thisLock (fun () ->
                        let key1 = key
                        key <- key + 1
                        subscriptions <- subscriptions.Add(key1, obs)
                        key1)
                { new IDisposable with  
                    member this.Dispose() = 
                        lock thisLock (fun () -> 
                            subscriptions <- subscriptions.Remove(key1)) } }

    let mutable finished = false 
    let checkFinished () = if finished then failwith "IObserver is already finished"
    // The source ought to call these methods in serialized fashion (from 
    // any thread, but serialized and non-reentrant). 
    member this.Next(obs) =
        checkFinished ()
        next obs

    member this.Completed() =
        checkFinished ()
        finished <- true
        completed()

    member this.Error(err) =
        checkFinished ()
        finished <- true
        error err

    // The IObservable object returned is thread-safe; you can subscribe  
    // and unsubscribe (Dispose) concurrently. 
    member this.AsObservable = obs 

module Task =
    open System.Threading
    open System.Threading.Tasks
    open Yaaf.FSharp.Control
    open Yaaf.FSharp.Functional
   
    let inline reraise (e:System.Exception) =
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw()
        raise e
         
    [<Obsolete>]
    let inline reraisePreserveStackTrace (e:System.Exception) = reraise e
    
    let flatAggregate (agg:AggregateException) = 
        let flat = agg.Flatten()
        if flat.InnerExceptions.Count = 1 then
            flat.InnerExceptions.[0]
        else flat :> exn
    let flatten (exn:exn) = 
        match exn with
        | :? AggregateException as agg ->
            flatAggregate agg
        | _ -> exn
    let ofPlainTask (t:System.Threading.Tasks.Task) = 
        if (t.Status = System.Threading.Tasks.TaskStatus.Created) then
            t.Start()
        t.ContinueWith(
            new System.Func<System.Threading.Tasks.Task, unit>(
                fun t -> 
                    if t.IsFaulted then 
                        reraise <| flatAggregate t.Exception))

    let startTask o (errors:Event<System.EventHandler<_>,exn>) asy =
        let t = asy |> Async.StartAsTask
        t.ContinueWith(
            new System.Action<System.Threading.Tasks.Task<unit>>
                (fun (t:System.Threading.Tasks.Task<unit>) -> 
                    if t.IsFaulted then
                        errors.Trigger(o, flatAggregate t.Exception))) |> ignore
    let guardAggregate f =
        try
            f()
        with
        | :? AggregateException as agg -> reraise (flatAggregate agg)
    let start (t:Task<_>) = t.Start()
    let result (t:Task<_>) = 
        if t = null then raise <| NullReferenceException("Task is null!")
        guardAggregate (fun () -> t.Result)
    let wait (t:Task) = 
        if t = null then raise <| NullReferenceException("Task is null!")
        guardAggregate t.Wait 
    let await (t:Task<_>) = 
      if t = null then raise <| NullReferenceException("Task is null!")
      async {
        try
            if t.IsCompleted then
                // For Async.StartImmediate we dont want to loose synchronicity for a already finished task!
                return t.Result 
            else
                return! t |> Async.AwaitTask
        with
        | :? AggregateException as agg ->
            return reraise <| flatAggregate agg 
      } 
    let awaitNoExn (t:Task<_>) =
        if t = null then raise <| NullReferenceException("Task is null!")
        Async.FromContinuations(fun (cont, econt, ccont) -> t.ContinueWith(fun (t:System.Threading.Tasks.Task<_>) -> cont t) |> ignore)
    let awaitNoExnIgnore (t:Task<_>) =
        if t = null then raise <| NullReferenceException("Task is null!")
        Async.FromContinuations(fun (cont, econt, ccont) -> t.ContinueWith(fun (t:System.Threading.Tasks.Task<_>) -> cont ()) |> ignore)

    let awaitPlain (t:System.Threading.Tasks.Task) = 
        if t = null then raise <| NullReferenceException("Task is null!")
        t |> ofPlainTask |> await
      
    let runWork (f:unit -> 'a) = System.Threading.Tasks.Task.Run(new System.Func<'a>(f)) 
    let runWorkTask (f:unit -> System.Threading.Tasks.Task<'a>) = System.Threading.Tasks.Task.Run<'a>(new System.Func<_>(f)) 

    //let map f (t:Task<_>) =         
    //    if t.Status = TaskStatus.Created then
    //        new Task<_>(fun () ->
    //            let sched = TaskScheduler.Current
    //            t.Start(sched)
    //            f t.Result)
    //    else
    //        t.ContinueWith(fun (t:Task<_>) -> t.Result |> f)
            
    let continueWith (f:Task<_> -> _) (t:Task<_>) = 
        t.ContinueWith(f)
        
    let ofAsync asy = 
        if obj.ReferenceEquals(asy, null) then raise <| NullReferenceException("Async is null!")
        new Task<_>(fun () -> 
            let t = asy |> Async.StartAsTask
            t.Result)
        
    /// Starts the given tasks and returns them in the order they finish.
    /// The tasks will be started as soon as they are available and they will be returned as they finish.
    /// The task will start when you start to iterate over the results.
    let startTasks (waitForTasks:AsyncSeq<Task<_>>) = 
      asyncSeq {
        let source = new ObservableSource<_>()
        // Wait for the tasks to become available
        // Start tasks
        async {
            // Wait for all tasks to finish to send complete
            
            // Wait a time to let them attach to source
            do! Async.Sleep 10
            let cache =
                waitForTasks
                    // NOTE: maybe we should do it with "Task.map" but I don't feel like the above map implementation is good enough.
                    |> AsyncSeq.map (fun task -> 
                        if task.Status = TaskStatus.Created then
                            start task
                        task)
                    |> AsyncSeq.map  
                        (continueWith
                            (fun t -> source.Next t))
                                //if t.IsFaulted then source.Error t.Exception
                                //if t.IsCompleted && not t.IsFaulted then source.Next t.Result))
                    |> AsyncSeq.cache
            // Start all tasks
            do! cache 
                    |> AsyncSeq.iter (fun _ -> ())
            
            // Wait for all to finish
            do! cache
                    |> AsyncSeq.iterAsync (await >> Async.Ignore)
            source.Completed()
        } |> Async.Start
        // Note here we start to listen on the source
        yield! source.AsObservable |> AsyncSeq.ofObservableBuffered }

    // ---------------------------------
    // TASK COMPUTATION
    // ---------------------------------

    /// Task result
    type Result<'T> = 
        /// Task was canceled
        | Canceled
        /// Unhandled exception in task
        | Error of exn 
        /// Task completed successfully
        | Successful of 'T

    let run (t: unit -> Task<_>) = 
        try
            let task = t()
            task.Result |> Result.Successful
        with 
        | :? OperationCanceledException -> Result.Canceled
        | :? AggregateException as e ->
            match e.InnerException with
            | :? TaskCanceledException -> Result.Canceled
            | _ -> Result.Error e
        | e -> Result.Error e

    let toAsync (t: Task<'T>): Async<'T> =
        if obj.ReferenceEquals(t, null) then raise <| NullReferenceException("Task is null!")
        let abegin (cb: AsyncCallback, state: obj) : IAsyncResult = 
            match cb with
            | null -> upcast t
            | cb -> 
                t.ContinueWith(fun (_ : Task<_>) -> cb.Invoke t) |> ignore
                upcast t
        let aend (r: IAsyncResult) = 
            (r :?> Task<'T>).Result
        Async.FromBeginEnd(abegin, aend)

    /// Transforms a Task's first value by using a specified mapping function.
    let inline mapWithOptions (token: CancellationToken) (continuationOptions: TaskContinuationOptions) (scheduler: TaskScheduler) f (m: Task<_>) =
        m.ContinueWith((fun (t: Task<_>) -> f t.Result), token, continuationOptions, scheduler)

    /// Transforms a Task's first value by using a specified mapping function.
    let inline map f (m: Task<_>) = 
        m.ContinueWith(fun (t: Task<_>) -> f t.Result)

    let inline bindWithOptions (token: CancellationToken) (continuationOptions: TaskContinuationOptions) (scheduler: TaskScheduler) (f: 'T -> Task<'U>) (m: Task<'T>) =
        m.ContinueWith((fun (x: Task<_>) -> f x.Result), token, continuationOptions, scheduler).Unwrap()

    let inline bind (f: 'T -> Task<'U>) (m: Task<'T>) = 
        m.ContinueWith(fun (x: Task<_>) -> f x.Result).Unwrap()

    let inline returnM a = 
        let s = TaskCompletionSource()
        s.SetResult a
        s.Task
        

    /// Sequentially compose two actions, passing any value produced by the first as an argument to the second.
    let inline (>>=) m f = bind f m

    /// Flipped >>=
    let inline (=<<) f m = bind f m

    /// Sequentially compose two either actions, discarding any value produced by the first
    let inline (>>.) m1 m2 = m1 >>= (fun _ -> m2)

    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g

    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x

    /// Promote a function to a monad/applicative, scanning the monadic/applicative arguments from left to right.
    let inline lift2 f a b = 
        a >>= fun aa -> b >>= fun bb -> f aa bb |> returnM

    /// Sequential application
    let inline ap x f = lift2 id f x

    /// Sequential application
    let inline (<*>) f x = ap x f

    /// Infix map
    let inline (<!>) f x = map f x

    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) a b = lift2 (fun _ z -> z) a b

    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) a b = lift2 (fun z _ -> z) a b
    
    type TaskBuilder(?continuationOptions, ?scheduler, ?cancellationToken) =
        let contOptions = defaultArg continuationOptions TaskContinuationOptions.None
        let scheduler = defaultArg scheduler TaskScheduler.Default
        let cancellationToken = defaultArg cancellationToken CancellationToken.None

        member this.Return x = returnM x

        member this.Zero() = returnM ()

        member this.ReturnFrom (a: Task<'T>) = a

        member this.Bind(m, f) = bindWithOptions cancellationToken contOptions scheduler f m

        member this.Combine(comp1, comp2) =
            this.Bind(comp1, comp2)

        member this.While(guard, m) =
            if not(guard()) then this.Zero() else
                this.Bind(m(), fun () -> this.While(guard, m))

        member this.TryFinally(m, compensation) =
            try this.ReturnFrom m
            finally compensation()

        member this.Using(res: #IDisposable, body: #IDisposable -> Task<_>) =
            this.TryFinally(body res, fun () -> match res with null -> () | disp -> disp.Dispose())

        member this.For(sequence: seq<_>, body) =
            this.Using(sequence.GetEnumerator(),
                                 fun enum -> this.While(enum.MoveNext, fun () -> body enum.Current))

        member this.Delay (f: unit -> Task<'T>) = f

        member this.Run (f: unit -> Task<'T>) = f()
    //type FSTask<'a> = 
    type TaskBuilderContext<'context>(getContinuationOptions, getScheduler, getToken) =
        //let contOptions = defaultArg continuationOptions TaskContinuationOptions.None
        //let scheduler = defaultArg scheduler TaskScheduler.Default
        //let token = defaultArg token CancellationToken.None

        let lift (t: Task<_>) = fun (_: 'context) -> t
        let bind (t: 'context -> Task<'T>) (f: 'T -> ('context -> Task<'U>)) =
            fun (context: 'context) ->
                (t context).ContinueWith((fun (x: Task<_>) -> f x.Result context), getToken context, getContinuationOptions context, getScheduler context).Unwrap()

        member this.Return x = lift (returnM x)

        member this.ReturnFrom t = lift t

        member this.ReturnFrom (t: 'context -> Task<'T>) = t

        member this.Zero() = this.Return ()

        member this.Bind(t, f) = bind t f            

        member this.Bind(t, f) = bind (lift t) f                

        member this.Combine(t1, t2) = bind t1 (konst t2)        

        member this.While(guard, m) =
                if not(guard()) then 
                    this.Zero()
                else
                    bind m (fun () -> this.While(guard, m))                    

        member this.TryFinally(t : 'context -> Task<'T>, compensation) =
            try t
            finally compensation()

        member this.Using(res: #IDisposable, body: #IDisposable -> ('context -> Task<'T>)) =
            this.TryFinally(body res, fun () -> match res with null -> () | disp -> disp.Dispose())

        member this.For(sequence: seq<'T>, body) =            
                this.Using(sequence.GetEnumerator(),
                                 fun enum -> this.While(enum.MoveNext, fun token -> body enum.Current token))
        
        member this.Delay f = this.Bind(this.Return (), f)
    let Context = returnM

    type TaskBuilderWithToken(?continuationOptions, ?scheduler) =
        inherit TaskBuilderContext<CancellationToken>(
            konst (defaultArg continuationOptions TaskContinuationOptions.None), 
            konst (defaultArg scheduler TaskScheduler.Default), 
            id)
            
    let Token = (fun (token:CancellationToken) -> returnM token)
    let taskBuilder = new TaskBuilderWithToken()
    let task = new TaskBuilder()

    let inline whenAll (tasks: Task<_> seq) =
        Task.WhenAll(tasks)

    let inline whenAny (tasks: Task<_> seq) =
        Task.WhenAny(tasks) |> Task.map (fun res -> res.Result)

    let inline combine (m1: Task<_>) (m2: Task<_>) =
        task {
            let! r1 = m1
            let! r2 = m2
            return r1, r2
        }
        //[ m1 |> Task.map Choice1Of2; m2  |> Task.map Choice2Of2 ] 
        //|> whenAll 
        //|> Task.map 
        //    (fun results -> 
        //        (match results.[0] with
        //        | Choice1Of2 r -> r
        //        | _ -> invalidOp "Unexpected return type of task1"),
        //        (match results.[1] with
        //        | Choice2Of2 r -> r
        //        | _ -> invalidOp "Unexpected return type of task2"))
    let inline ignore (m: Task<_>) = m |> Task.map ignore