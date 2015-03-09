// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.Helper
open Yaaf.FSharp.Control
open FSharpx
open FSharpx.Collections

open System
open System.Collections.Generic
open System.Threading
open Yaaf.FSharp.Functional
open System


[<AutoOpen>]
module Core =
    type System.Collections.Concurrent.ConcurrentDictionary<'k, 'v> with
        member x.TryRemove(key:'k, value:'v) =
            (x :> ICollection<_>).Remove(KeyValuePair(key, value))

    open System.Threading.Tasks
    let task = Task.TaskBuilder()
    let taskC = Task.TaskBuilderWithToken()
    let taskBuilder cts = Task.TaskBuilder(cancellationToken = cts)
    let task1 = 
        task {
            let! item = 
                Task.Factory.StartNew(fun () -> 
                    for i in 1 .. 2000 do ignore ()
                    "t")
            return item + "u"
        }
    let task2 = 
        taskC {
            let! token = (fun token -> task.Return token)
            let! item = 
                Task.Factory.StartNew(fun () -> 
                    for i in 1 .. 2000 do ignore ()
                    "t")
            return item + "u"
        }

    let isMono =
        System.Type.GetType ("Mono.Runtime") <> null

    let (|Equals|_|) arg x = 
      if (arg = x) then Some() else None
    let (|StartsWith|_|) arg (x:string) = 
      if (x.StartsWith(arg)) then Some() else None



open System.Reflection
module Option =
    let fromNullable s=
        if s = null then None else Some s
        

module AsyncSeq =
    // Waits until all values are available
    let await s = async {
        let! results =
            s |> AsyncSeq.fold (fun state item -> item :: state) []
        return 
            results |> List.rev |> List.toSeq }


module Event =
    /// Executes f just after adding the event-handler
    let guard f (e:IEvent<'Del, 'Args>) = 
        let e = Event.map id e
        { new IEvent<'Args> with 
            member x.AddHandler(d) = e.AddHandler(d); f()
            member x.RemoveHandler(d) = e.RemoveHandler(d)
            member x.Subscribe(observer) = 
              let rm = e.Subscribe(observer) in f(); rm }

    /// <summary>
    /// Starts awaiting the given event, 
    /// but makes sure that the event was actually added (within the new Task) before returning.
    /// </summary>
    /// <param name="ev"></param>
    let guardAwait (ev :IEvent<_,_>) = 
        async {
            let e = new AsyncManualResetEvent()
            let event =
                ev
                |> guard e.Set
                |> Async.AwaitEvent
                |> Async.StartAsTask
            do! e.AsAsync
            return event
        }
    let skipWhile f (ev:IEvent<_,_>) = 
        let skipFinished = ref false
        ev
            |> Event.choose (fun item -> 
                if not !skipFinished && f item then
                    None 
                else 
                    skipFinished := true
                    Some item)

module Observable =
  let guard f (e:IObservable<'Args>) = 
    { new IObservable<'Args> with 
        member x.Subscribe(observer) = 
          let rm = e.Subscribe(observer) in f(); rm }

type VolatileBarrier() =
    [<VolatileField>]
    let mutable isStopped = false
    member __.Proceed = not isStopped
    member __.Stop() = isStopped <- true
module Async =

    let StartAsTaskGuarded (f) = 
        async {
            let e = new AsyncManualResetEvent()
            let event =
                f e.Set
                |> Async.StartAsTask
            do! e.AsAsync
            return event
        }
    let StartChildGuarded (f) = 
        async {
            let! child = StartAsTaskGuarded f
            return child |> Task.await
        }

        
    let map f a = async { let! b = a
                          return b |> f }
    
    let invoke f a = 
        async { 
            let! b = a
            f b
            return b
        }
    
    let zip a = async { let! b = a
                        let! c = b
                        return c }

    let reraise e = Task.reraisePreserveStackTrace e

[<AutoOpen>]
module AsyncExtensions =

    open System.Threading
    open System.Threading.Tasks
    let internal startAsTaskImmediate (token:CancellationToken, computation : Async<_>, taskCreationOptions) : Task<_> =
        if obj.ReferenceEquals(computation, null) then raise <| NullReferenceException("computation is null!")
        let taskCreationOptions = defaultArg taskCreationOptions TaskCreationOptions.None
        let tcs = new TaskCompletionSource<_>(taskCreationOptions)

        // The contract: 
        //      a) cancellation signal should always propagate to task
        //      b) CancellationTokenSource that produced a token must not be disposed until the the task.IsComplete
        // We are:
        //      1) registering for cancellation signal here so that not to miss the signal
        //      2) disposing the registration just before setting result/exception on TaskCompletionSource -
        //              otherwise we run a chance of disposing registration on already disposed  CancellationTokenSource
        //              (See (b) above)
        //      3) ensuring if reg is disposed, we do SetResult
        let barrier = VolatileBarrier()
        let reg = token.Register(fun _ -> if barrier.Proceed then tcs.SetCanceled())
        let task = tcs.Task
        let disposeReg() =
            barrier.Stop()
            if not (task.IsCanceled) then reg.Dispose()

        let a = 
            async { 
                try
                    let! result = computation
                    do 
                        disposeReg()
                        tcs.TrySetResult(result) |> ignore
                with
                |   e -> 
                        disposeReg()
                        tcs.TrySetException(e) |> ignore
            }
        Async.StartImmediate(a, token)
        task
    type Async with
        static member StartAsTaskImmediate (computation,?taskCreationOptions,?cancellationToken)=
            let token = defaultArg cancellationToken Async.DefaultCancellationToken       
            startAsTaskImmediate(token,computation,taskCreationOptions)
module Lazy = 
    let force (l:_ System.Lazy) =
        l.Force()

module Seq =   
    /// Builds the lazy sequence and caches its results (like cache but iterates the sequence once)
    let build seq =
        let cached = seq |> Seq.cache
        cached |> Seq.iter (fun _ -> ())
        cached
        
    /// Tries to get the head and throws the given exception if the head is not available
    let headExn (exn:Lazy<_>) seq =
        match seq |> Seq.tryHead with
        | Some s -> s
        | None -> raise exn.Value

    /// Tries find an item and throws the given exception if nothing was found
    let findExn (exn:Lazy<_>) f seq =
        match seq |> Seq.tryFind f with
        | Some s -> s
        | None -> raise exn.Value
    
    type OneNoneMany<'a> = 
        | ExactlyOne of 'a
        | NoItems
        | Many
        
    /// Checks if the given sequence has exactly one, none or many elements.
    let tryExactlyOneOrNone (source : seq<_>) =
        use e = source.GetEnumerator()
        if e.MoveNext()
        then 
            let head = e.Current
            if e.MoveNext()
            then Many
            else ExactlyOne head
        else NoItems //empty list
        
    /// Tries to get exactly one item from the sequence and returns None if the sequence has more or none items.
    let tryExactlyOne (source : seq<_>) =   
        match source |> tryExactlyOneOrNone with
        | ExactlyOne head -> Some head
        | _ -> None
        

    /// Tries to get exactly one item from the sequence and throws the given exception if the sequence has more or none items.
    let exactlyOneExn (exn:Lazy<_>) seq =
        match seq |> tryExactlyOne with
        | Some s -> s
        | None -> raise (exn.Value)
    
    /// Tries to get exactly one item from the sequence and throws the given exception if the sequence has more items.
    let exactlyOneOrNoneExn (exn:Lazy<_>) seq =
        match seq |> tryExactlyOneOrNone with
        | OneNoneMany.ExactlyOne s -> Some s
        | OneNoneMany.NoItems -> None
        | OneNoneMany.Many -> raise (exn.Value)

    let duplicates items =
      seq {
        let d = System.Collections.Generic.Dictionary()
        for i in items do
            match d.TryGetValue(i) with
            | false,_    -> d.[i] <- false         // first observance
            | true,false -> d.[i] <- true; yield i // second observance
            | true,true  -> ()                     // already seen at least twice
      }