// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.IO

open Yaaf.FSharp.Control
open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Threading

open Yaaf.Helper

type ConcurrentQueueMessage<'a> =
    | Enqueue of 'a * AsyncReplyChannel<exn option>
    | Dequeue of AsyncReplyChannel<Choice<'a, exn>>
    | TryDequeue of AsyncReplyChannel<Choice<'a option,exn>>

type ConcurrentQueue<'a>() =
    let core =
        let queue = Queue<'a>()
        let waitingQueue = Queue<AsyncReplyChannel<Choice<'a, exn>>>()
        MailboxProcessor.Start(fun inbox ->            
            let rec loop () = async {
                let! msg = inbox.Receive()                
                match msg with
                | Enqueue (item,reply) ->
                    try
                        if waitingQueue.Count > 0 then
                            let waiting = waitingQueue.Dequeue()
                            waiting.Reply (Choice1Of2 item)
                        else
                            queue.Enqueue item
                        reply.Reply None
                    with exn ->
                        reply.Reply (Some exn)
                | Dequeue reply ->
                    try
                        if queue.Count > 0 then
                            let item = queue.Dequeue()
                            reply.Reply (Choice1Of2 item)
                        else
                            waitingQueue.Enqueue reply
                    with exn ->
                        reply.Reply (Choice2Of2 exn)
                | TryDequeue reply ->
                    try
                        let item = 
                            if queue.Count > 0 then
                                Some <| queue.Dequeue()
                            else None
                        reply.Reply (Choice1Of2 item)
                    with exn ->
                        reply.Reply (Choice2Of2 exn)
                return! loop() }
            loop())
    member x.EnqueueAsync(item) = async {
        let! item = core.PostAndAsyncReply(fun reply -> Enqueue (item, reply))
        return
            match item with
            | Some exn -> raise exn
            | None -> () }
    member x.DequeAsyncTimeout(?timeout) = async {
        let! result = 
            core.PostAndTryAsyncReply(
                (fun reply -> Dequeue (reply)), ?timeout = timeout)
        return
            match result with
            | Some r ->
                match r with
                | Choice1Of2 item -> Some item
                | Choice2Of2 exn -> raise exn
            | None -> None }
    member x.DequeAsync() = async {
        let! result = 
            core.PostAndAsyncReply(
                (fun reply -> Dequeue (reply)))
        return
            match result with
            | Choice1Of2 item -> item
            | Choice2Of2 exn -> raise exn }
            
    member x.TryDequeAsync() = async {
        let! result = core.PostAndAsyncReply(fun reply -> TryDequeue (reply))
        return
            match result with
            | Choice1Of2 item -> item
            | Choice2Of2 exn -> raise exn }
    member x.Enqueue(item) = x.EnqueueAsync item |> Async.RunSynchronously
    member x.Deque() = x.DequeAsync () |> Async.RunSynchronously
    member x.TryDeque() = x.TryDequeAsync () |> Async.RunSynchronously
            
type IStream<'a> =
    inherit IDisposable
    abstract member Read : unit -> Async<'a option>
    abstract member Write : 'a -> Async<unit>

type StreamHelper(istream:IStream<byte array>, finish:unit -> unit) =
    inherit Stream()
    let readLock = new obj()
    let mutable readRunning = false
    let mutable readingFailed = false
    let mutable cache = [||]
    let mutable currentIndex = 0
    let mutable isDisposed = false
    let mutable isFinished = false
    let doFinish () = 
        if not isFinished then
            finish()
            isFinished <- true
    let read (dst:byte array) offset count = 
        async {
        let onReadRunning () = 
            readingFailed <- true
            //Log.Err(fun () -> L "read already running:\n\t%s" (Log.AsyncStackString()))
            failwithf "can not have parallel reads"
        // Only one read allowed due to caching
        if (readRunning) then 
            onReadRunning()
        lock readLock (fun () ->
            if (readRunning) then 
                onReadRunning()
            readRunning <- true)
        try
            //Log.Verb(fun () -> L "Starting read")
            let! newCache = 
                if cache.Length - currentIndex > 0 then
                    async.Return cache
                else
                    async {
                    currentIndex <- 0
                    //Log.Verb(fun () -> L "waiting for istream read")
                    let! data = istream.Read()
                    //Log.Verb(fun () -> L "processing istream read data")
                    return
                        match data with
                        | Some d -> d
                        | None -> [||] }
            cache <- newCache
            // Use cache
            let realCount = 
                Math.Min(
                    cache.Length - currentIndex,
                    count)
            if realCount > 0 then
                for i in 0 .. realCount - 1 do
                    dst.[offset + i] <- cache.[currentIndex + i]
                currentIndex <- currentIndex + realCount               
            return if realCount < 0 then 0 else realCount
        finally
            readRunning <- false
            if (readingFailed) then
                //Log.Warn(fun () -> L "other read failed, here is my trace: \n%s" (Log.AsyncStackString ()))
                readingFailed <- false  } //|> Log.TraceMe
    let write dst offset count = async {
        if count > 0 then
            let newDst =
                    Array.sub dst offset count
            return! istream.Write newDst
        else
            //Log.Warn(fun () -> L "write of length 0\nAsyncStack: %s" (Log.AsyncStackString ()))
            return ()
            // Mono sslstream writes 0 bytes at the beginning
            //return doFinish() 
        }
    let readOne () = async {
        let dst = Array.zeroCreate 1
        let! result = read dst 0 1
        return
            if result = 0 then
                None
            else Some (dst.[0]) }
            
    let writeOne b = istream.Write [|b|]
    let beginRead, endRead, cancelRead = 
        Async.AsBeginEnd(fun (dst, offset, count) -> read dst offset count)
    let beginWrite, endWrite, cancelWrite = 
        Async.AsBeginEnd(fun (src, offset, count) -> write src offset count)
        
    let checkDisposed() =
        if isDisposed then
            raise <| ObjectDisposedException("onetimestream")
    override x.Flush () = ()        
    override x.Seek(offset:int64, origin:SeekOrigin) =
        raise <| NotSupportedException()        
    override x.SetLength(value:int64) =
        raise <| NotSupportedException()    
    override x.ReadAsync (dst, offset, count, cancel) = Async.StartAsTask(read dst offset count, cancellationToken = cancel)
    override x.WriteAsync (dst, offset, count, cancel) = Async.StartAsTask(write dst offset count, cancellationToken = cancel) :> _
    member x.BeginRead(dst, offset, count, callback, state) =
        checkDisposed()
        beginRead((dst, offset, count), callback, state)        
    member x.EndRead(res) =
        checkDisposed()
        endRead res            
    member x.BeginWrite(src, offset, count, callback, state) =
        checkDisposed()
        beginWrite((src, offset, count), callback, state)
    member x.EndWrite(res) =
        checkDisposed()
        endWrite res
    override x.Read(dst, offset, count) = 
        checkDisposed()
        read dst offset count |> Async.RunSynchronously            
    override x.Write(src, offset, count) = 
        checkDisposed()
        write src offset count |> Async.RunSynchronously
    override x.ReadByte() =
        if isDisposed then -1
        else
            match readOne() |> Async.RunSynchronously with
            | Some s -> int s
            | None -> -1
    override x.WriteByte item =
        if not isDisposed then
            writeOne item |> Async.RunSynchronously        
    override x.CanRead 
        with get() = 
            checkDisposed()
            true        
    override x.CanSeek
        with get() =
            checkDisposed()
            false                
    override x.CanWrite
        with get() =
            checkDisposed()
            true        
    override x.Length
        with get() =
            raise <| NotSupportedException()                
    override x.Position
        with get() =
            raise <| NotSupportedException()
        and set value =            
            raise <| NotSupportedException()             
    override x.Dispose disposing =
        if not isDisposed then
            //Log.Verb(fun () -> L "disposing istream \nAsyncStack: %s" (Log.AsyncStackString ()))
            isDisposed <- true
            if disposing then
                doFinish()
                istream.Dispose()
        base.Dispose disposing


type PeekMoreOption<'a> = 
    | Success of 'a array
    | Part of int * 'a array
    with 
        member x.Length 
            with get() =
                match x with
                | Success d -> d.Length
                | Part (i,_) -> i
        member x.Data 
            with get() =
                match x with
                | Success d 
                | Part (_,d) -> d
                    
        member x.IsComplete 
            with get() =
                match x with
                | Success _ -> true
                | Part _ -> false

type IPeekStream<'a> =
    inherit IStream<'a>
    /// Completly clears the state and makes the stream unmodified
    abstract member ResetAll : unit -> Async<unit>
    abstract member ResetOne : unit -> Async<unit>
    abstract member ResetMore : int -> Async<unit>
    abstract member PeekNext : unit -> Async<'a option>
    abstract member PeekMore : int -> Async<'a PeekMoreOption>
    abstract member ReadMore : int -> Async<'a array>
    abstract member ReadOne : unit -> Async<'a>
    abstract member TryReadMore : int -> Async<'a PeekMoreOption>
    // TryReadOne in IStream => Read()


/// Note that this type is only for performance reasons
/// Because the IStream is an asynchronous interface there will be no stackoverflows
/// However it will slow down with each abstraction...
type WrapperIStream<'a> (wrapped : IStream<'a>, canUnwrap, unwrapped : IStream<'a>) = 
    let mutable wrapped = wrapped
    let mutable canUnwrap = canUnwrap
    let mutable unwrapped = Some unwrapped
    let rec checkUnwrap () =
        match unwrapped with
        | Some toUnwrap when canUnwrap() ->
            match toUnwrap with
            | :? WrapperIStream<'a> as again ->
                canUnwrap <- again.CanUnwrap
                wrapped <- again.Wrapped 
                unwrapped <- again.UnWrapped 
                checkUnwrap () // check again!
            | _ -> 
                wrapped <- toUnwrap
                canUnwrap <- (fun () -> false)
                unwrapped <- None
        | _ -> ()
       
    interface IStream<'a> with
        member x.Read () = 
            checkUnwrap() // only read is important
            wrapped.Read ()
        member x.Write item = wrapped.Write item
        member x.Dispose() = 
            wrapped.Dispose()
            match unwrapped with
            | Some u -> u.Dispose()
            | None -> ()
            
    member x.Wrapped with get() = wrapped
    member x.UnWrapped with get() = unwrapped
    member x.CanUnwrap with get() = canUnwrap


       
module Stream =
    let fromInterfaceAdvanced finish istream  = new StreamHelper(istream, finish) :> Stream
    let fromInterfaceSimple istream = fromInterfaceAdvanced (fun () -> ()) istream
    let fromInterface istream = fromInterfaceSimple istream

    let fromReadWriteDispose dis read write = 
        { new IStream<_> with
            member x.Read () =
                read()
            member x.Write item = 
                write item
          interface IDisposable with
            member x.Dispose () = dis () }
    let fromReadWrite read write = fromReadWriteDispose id read write
    let toInterface buffersize (stream:System.IO.Stream) = 
        let buffer = Array.zeroCreate buffersize
        let read () = async {            
            let! read = stream.AsyncRead(buffer, 0, buffer.Length)
            let readData =
                Array.sub buffer 0 read
            return
                if readData.Length > 0 then
                    Some readData
                else None}
        let write src = stream.AsyncWrite(src, 0, src.Length)
        fromReadWriteDispose stream.Dispose read write
    let toInterfaceText buffersize (stream:System.IO.TextReader) = 
        let buffer = Array.zeroCreate buffersize
        let read () = async {
            let! read = stream.ReadAsync(buffer, 0, buffer.Length)
            let readData =
                Array.sub buffer 0 read
            return
                if readData.Length > 0 then
                    Some readData
                else None}
        let write src =
            failwith "writing in TextReader not supported" 
            // writer.WriteAsync(src, 0, src.Length)
        fromReadWriteDispose stream.Dispose read write
    let toMaybeRead read = async {
        let! data = read
        return Some data }
        
        
    let infiniteStream () = 
        let queue = new ConcurrentQueue<'a>()
        fromReadWrite (fun () -> toMaybeRead (queue.DequeAsync ())) queue.EnqueueAsync

    let toLimitedStream (raw:IStream<_>) = 
        //let raw = infiniteStream()
        let readFinished = ref false
        let read () =
            if !readFinished then
               async.Return None
            else 
              async {
                let! data = raw.Read()
                return
                    match data with
                    | Some s -> 
                        match s with
                        | Some d -> Some d
                        | None -> 
                            readFinished := true
                            None
                    | None -> 
                        failwith "stream should not be limited as we are using an infiniteStream!" }
        let isFinished = ref false
        let finish () = async {
            do! raw.Write None
            isFinished := true }
        let write item = 
            if !isFinished then
                failwith "stream is in finished state so it should not be written to!"
            raw.Write (Some item)
        finish,
        fromReadWriteDispose raw.Dispose read write
    let limitedStream () = toLimitedStream (infiniteStream())
    let buffer (stream:IStream<_>) =        
        let queue = infiniteStream()
        let write item = async {            
            do! queue.Write item
            do! stream.Write item }
        fromReadWrite queue.Read (fun item -> invalidOp "Write is not allowed"),
        fromReadWriteDispose stream.Dispose stream.Read write

    let combineReadAndWrite (readStream:IStream<_>) (writeStream:IStream<_>) = 
        fromReadWrite readStream.Read writeStream.Write

    let crossStream (s1:IStream<_>) (s2:IStream<_>) = 
        combineReadAndWrite s1 s2,
        combineReadAndWrite s2 s1
    let map f g (s:IStream<_>) = 
        let read () =  async {
            let! read = s.Read()
            return f read }
        let write item = s.Write (g item)
        fromReadWriteDispose s.Dispose read write
        
    let filterRead f (s:IStream<_>) =         
        let rec read () = async {
            let! data = s.Read()
            return!
                if f data then
                    async.Return data
                else
                    read () }
        fromReadWriteDispose s.Dispose read s.Write

    let filterWrite f (s:IStream<_>) = 
        let write item = 
            if f item then
                s.Write (item)
            else async.Return ()
        fromReadWriteDispose s.Dispose s.Read write
        
    let readOnly (s:IStream<_>) = fromReadWriteDispose s.Dispose s.Read (fun elem -> raise <| new NotSupportedException("write not supported"))
    let writeOnly (s:IStream<_>) = fromReadWriteDispose s.Dispose (fun () -> raise <| new NotSupportedException("write not supported")) s.Write
    /// Duplicates the given stream, which means returning two stream instances
    /// which will read the same data. 
    /// At the same time buffers all data (ie read from s as fast as possible).
    /// Any data written to the returned instances will be written to the given instance.
    let duplicate (s:IStream<_>) = 
        let close1, s1 = limitedStream()
        let close2, s2 = limitedStream()
        async { //TODO: add exception handling?
            while true do
                let! data = s.Read()
                match data with
                | Some item ->
                    do! s1.Write item
                    do! s2.Write item
                | None ->
                    do! close1()
                    do! close2() } |> Async.Start
        combineReadAndWrite s1 s,
        combineReadAndWrite s2 s

    let split f s = 
        let s1, s2 = duplicate s
        s1 |> filterRead f,
        s2 |> filterRead (not << f)
    let toSeq (s:IStream<_>) =       
      asyncSeq {
        let isFinished = ref false
        while not !isFinished do
            let! data = s.Read()
            match data with
            | Some item ->
                yield item
            | None -> isFinished := true }
    let ofSeq write (s:AsyncSeq<_>) = 
        let current = ref s
        let read () = async {
            let! next = !current
            return
                match next with
                | Nil -> 
                    None
                | Cons(item, next) ->
                    current := next
                    Some item }            
        fromReadWrite read write

    let maybeAppendFront data (s:IStream<_>) = 
        let first = ref true
        let canUnwrap = ref false
        let ev = new AsyncManualResetEvent()
        let rec read () = 
            if !first then 
                first := false
                async {
                let! d = data
                match d with
                | Some e ->
                    ev.Set()
                    return d
                | None -> 
                    let! data = s.Read()
                    ev.Set()
                    return data }
            else 
                async {
                do! ev.AsAsync
                canUnwrap := true
                return! s.Read()
                }
        let tempState = fromReadWriteDispose s.Dispose read s.Write
        new WrapperIStream<_>(tempState, (fun () -> !canUnwrap), s) :> IStream<_>
    let appendFront data (s:IStream<_>) = 
        let newData = async { let! d = data in return Some d; }
        maybeAppendFront newData s
    
type PeekStream<'a> (multiStream:IStream<'a array>) =
    let arrayBufferLength = ref 0
    let arrayBuffer = ref [||]
    let pos = ref 0
    let innerStreamFinished = ref false
    let disposed = ref false
    let rec readSingle () = 
        if !disposed then failwith "object disposed"
        async {
            assert ((!arrayBufferLength) = (!arrayBuffer).Length)
            match !pos < (!arrayBuffer).Length with
            | true -> // data left to read
                let data = (!arrayBuffer).[!pos]
                pos := !pos + 1
                return Some data
            | false -> // copy new data to arrayBuffer
                let! nextData = multiStream.Read()
                match nextData with
                | None ->
                    innerStreamFinished := true
                    return None //finished
                | Some data ->
                    if data.Length > 0 then
                        // cp data to arraybuffer && resize
                        if !arrayBufferLength = 0 then
                            pos := 0
                            arrayBuffer := data
                            arrayBufferLength := data.Length
                        else
                            let newLen = Math.Max(!arrayBufferLength * 2, !arrayBufferLength + data.Length)
                            let newArrayBuffer = Array.zeroCreate newLen
                            Array.Copy(!arrayBuffer, newArrayBuffer, !arrayBufferLength)
                            Array.Copy(data, 0, newArrayBuffer, !arrayBufferLength, data.Length)
                                
                            arrayBuffer := newArrayBuffer
                            arrayBufferLength := newLen

                        return! readSingle()
                    else // ? finished, but generally this doesn't happen
                        innerStreamFinished := true
                        return None
            }

    let writeSingle data = 
        failwith "write not supported"
    let readMoreHelper count =
        async {
            let result = Array.zeroCreate count
            let streamFinished = ref false
            let readCount = ref 0
            for i in 0 .. count - 1 do
                if not !streamFinished then
                    let! read = readSingle()
                    match read with
                    | Some s -> 
                        readCount := !readCount + 1
                        result.[i] <- s
                    | None -> streamFinished := true
            return
                if !streamFinished then
                    Part (!readCount, result)
                else 
                    Success (result)
        }
    // TryReadOne
    member x.Read () = readSingle()
    member x.Write item = writeSingle item
    member x.Dispose() = ()
    interface IStream<'a> with
        member x.Read () = x.Read()
        member x.Write item = x.Write item
        member x.Dispose() = x.Dispose()
            
    member x.ResetAll () = 
        async { 
            pos := 0 }
    member x.ResetOne () = 
        async {
            if (!pos - 1 < 0) then
                failwith "invalid position" 
            pos := !pos - 1 }
    member x.ResetMore i = 
        async {
            if (!pos - i < 0) then
                failwith "invalid position"  
            pos := !pos - i }
        
    member x.TryReadMore count = readMoreHelper count

    member x.PeekNext () = 
        async { 
            let! data = x.Read()
            if data.IsSome then do! x.ResetOne()
            return data
        }

    member x.PeekMore count = 
        async {
            let! data = readMoreHelper count
            do! x.ResetMore (data.Length)
            return data
        }

    member x.ReadMore count =
        async {
            let! read = x.TryReadMore count
            return
                match read with
                | Success s -> s
                | Part _ -> failwith "ReadMore expected more data!"
        }
    member x.ReadOne () = 
        async {
            let! read = x.Read()
            return
                match read with
                | Some s -> s
                | None -> failwith "ReadOne expected data!"
        }

    interface IPeekStream<'a> with
        member x.ResetAll () = x.ResetAll()
        member x.ResetOne () = x.ResetOne()
        member x.ResetMore i = x.ResetMore i
        member x.PeekNext () = x.PeekNext()
        member x.PeekMore i = x.PeekMore i
        member x.ReadMore i = x.ReadMore i 
        member x.ReadOne () = x.ReadOne()
        member x.TryReadMore i = x.TryReadMore i
        
    member internal x.Continuation
        with get() = 
            let canUnwrap = ref false
            let returnState = 
                asyncSeq {
                    // read what is left in arrayBufer
                    let leftData = (!arrayBuffer).Length - !pos

                    // unsent data
                    if leftData > 0 then
                        let cp = Array.zeroCreate (leftData)
                        Array.Copy(!arrayBuffer, !pos, cp, 0, leftData)
                        canUnwrap := true
                        yield cp
                    else
                        // send others
                        canUnwrap := true
                
                    yield! multiStream |> Stream.toSeq                
                } |> Stream.ofSeq multiStream.Write
            new WrapperIStream<_>(returnState, (fun () -> !canUnwrap), multiStream) :> IStream<_>

[<AutoOpen>]
module StreamExtensions = 

    module Stream = 
        let handlePeek (multiStream:IStream<'a array>) f = 
          async {
            let peekStream = new PeekStream<_>(multiStream)
            let! result = f (peekStream:>IPeekStream<_>)
            return result, peekStream.Continuation }
    type StreamReader with
        member reader.ReadStringAsync() = 
          async {
            let buffer = Array.zeroCreate 1024
            let! read = reader.ReadAsync(buffer, 0, buffer.Length)
            return new String(Array.sub buffer 0 read)
          }


[<AutoOpen>]
module PeekStreamExtensions = 
    type IPeekStream<'a> with
        member x.ReadUntil f =
            async {
                let data = ref [] 
                let finished = ref false
                let streamFinished = ref false
                while not !finished do
                    let! read =  x.Read()
                    match read with
                    | Some d ->
                        if not (f d) then
                            finished := true
                        else
                            data := d :: !data                                
                    | None ->
                        streamFinished := true 
                        finished := true
            }
        member x.ReadLast () =
            async {
                do! x.ResetOne()
                return! x.ReadOne()
            }
        member x.ReadLastMore i =
            async {
                do! x.ResetMore i
                return! x.ReadMore i
            }

