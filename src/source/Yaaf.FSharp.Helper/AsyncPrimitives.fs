// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.Helper

open System.Threading
open System.Threading.Tasks
open System.Collections.Generic

type AsyncManualResetEvent() =
    let tcs = TaskCompletionSource<bool>()
    member x.WaitTask = tcs.Task
    member x.AsAsync = tcs.Task |> Task.await |> Async.Ignore
    member x.Set () = tcs.TrySetResult(true) |> ignore

type AsyncSemaphore (initialCount : int) =
    static let completed = Task.FromResult true
    let waiters = new Queue<TaskCompletionSource<bool>>()
    do 
        if initialCount < 0 then invalidArg "initialCount" "invalid range"
    let mutable currentCount = initialCount
    member x.WaitAsync () =
        lock waiters (fun () ->
            if currentCount > 0 then
                currentCount <- currentCount - 1
                completed
            else
                let waiter = new TaskCompletionSource<_>()
                waiters.Enqueue waiter
                waiter.Task)

    member x.Release () =
        let toRelease =
            lock waiters (fun () ->
                if waiters.Count > 0 then
                    Some <| waiters.Dequeue()
                else
                    currentCount <- currentCount + 1
                    None)
        match toRelease with
        | Some task -> task.SetResult true
        | None -> ()
type AsyncLock () =
    let sem = AsyncSemaphore(1)
    member x.Lock f =
        async {
            try
                do! sem.WaitAsync() |> Task.await |> Async.Ignore
                return! f ()
            finally
                sem.Release()
        }