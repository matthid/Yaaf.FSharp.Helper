// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Test.Yaaf.RefactorOut

open System.IO
open NUnit.Framework
open FsUnit
open Yaaf.IO
open Yaaf.Helper
open Yaaf.IO.Stream
open Yaaf.IO.StreamExtensions.Stream
open Yaaf.TestHelper

[<TestFixture>]
type IOStreamTest() = 
    inherit MyStreamTestClass()
    
    [<Test>]
    member this.``Check if singleByteStream allows single peeking``() = 
        let data = "someData"
        this.Write1(data)
        let result, unmodified = 
            (handlePeek this.StreamI1 (fun peek -> 
                 async { 
                     let! peekedData = peek.Read()
                     do! peek.ResetAll()
                     return peekedData
                 }))
            |> Async.RunSynchronously
        assert result.IsSome
        let reader = new StreamReader(unmodified |> fromInterfaceSimple)
        this.FinishStream1() // or next call will block
        reader.ReadToEnd() |> should be (equal data)
    
    [<Test>]
    member this.``Check if singleByteStream allows multiple peeking``() = 
        let data = "someData"
        this.Write1(data)
        let result, unmodified = 
            (handlePeek this.StreamI1 (fun peek -> 
                 async { 
                     let! peekedData = peek.Read()
                     let! peekedData = peek.Read()
                     let! peekedData = peek.Read()
                     let! peekedData = peek.Read()
                     let! peekedData = peek.Read()
                     do! peek.ResetAll()
                     return peekedData
                 }))
            |> Async.RunSynchronously
        assert result.IsSome
        let reader = new StreamReader(unmodified |> fromInterfaceSimple)
        this.FinishStream1() // or next call will block
        reader.ReadToEnd() |> should be (equal data)
    
    [<Test>]
    member this.``Check if singleByteStream resumes properly after peeking``() = 
        let data = "someData"
        this.Write1(data)
        let result, unmodified = (handlePeek this.StreamI1 (fun peek -> async { let! peekedData = peek.Read()
                                                                                let! peekedData = peek.Read()
                                                                                let! peekedData = peek.Read()
                                                                                return peekedData })) |> Async.RunSynchronously
        assert result.IsSome
        let reader = new StreamReader(unmodified |> fromInterfaceSimple)
        this.FinishStream1() // or next call will block
        reader.ReadToEnd() |> should be (equal (data.Substring(3)))
    
    [<Test>]
    member this.``Check if unwrapping works``() = 
        let data = "s"
        // write 2 times -> peek 1 -> read 2 times from returned
        let mutable current = this.StreamI1
        for i in 1..20 do
            current.Write([| 1uy |]) |> Async.RunSynchronously
            current.Write([| 2uy |]) |> Async.RunSynchronously
            let result, unmodified = 
                (handlePeek current (fun peek -> 
                     async { 
                         let! peekedData = peek.Read()
                         do! peek.ResetAll()
                         return peekedData
                     }))
                |> Async.RunSynchronously
            current <- unmodified
            let d1 = current.Read() |> Async.RunSynchronously
            let d2 = current.Read() |> Async.RunSynchronously
            d2 |> ignore
        let wrapper = current :?> WrapperIStream<byte array>
        wrapper.CanUnwrap() |> should be False
        wrapper.Wrapped.GetType() |> should not' (equal typeof<WrapperIStream<byte array>>)

[<TestFixture>]
type IOQueueTest() = 
    inherit MyStreamTestClass()
    
    [<Test>]
    member this.``Check that ConcurrentQueue returns finished stuff``() = 
        let testData = "testData"
        let q = ConcurrentQueue()
        let data = q.DequeAsync() |> Async.StartAsTask
        q.Enqueue(testData)
        data
        |> waitTaskIT "dataReturned" 1000
        |> should be (equal testData)
    
    [<Test>]
    member this.``Check that infiniteStream returns finished stuff``() = 
        let testData = "testData"
        let q = infiniteStream()
        let data = q.Read() |> Async.StartAsTask
        q.Write(testData) |> Async.RunSynchronously
        data
        |> waitTaskIT "dataReturned" 1000
        |> should be (equal (Some testData))
    
    [<Test>]
    member this.``Check that StringReader returns finished stuff``() = 
        let s1 = Stream.infiniteStream()
        let stream = Stream.fromInterfaceSimple s1
        let writer = new StreamWriter(stream)
        let reader = new StreamReader(stream)
        let buf = Array.zeroCreate 1024
        let read = reader.ReadAsync(buf, 0, buf.Length)
        let text = "<startElem att='test'>"
        writer.Write(text)
        writer.AutoFlush <- true
        writer.Flush()
        let result = read |> waitTaskIT "dataReturned" 1000
        result |> should be (equal text.Length)
        new System.String(buf, 0, result) |> should be (equal text)
