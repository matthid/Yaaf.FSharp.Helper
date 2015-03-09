// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Test.Yaaf.RefactorOut

open NUnit.Framework
open Yaaf.TestHelper
open Yaaf.Helper
open Swensen.Unquote

[<TestFixture>]
type TestWorkerThread() = 
    inherit MyTestClass()
    
    override x.Setup () =
      base.Setup()
#if FX_NO_CONTEXT
      WorkerThread.CallContext <-
        { new ICallContext with
            member __.LogicalGetData key =
                System.Runtime.Remoting.Messaging.CallContext.LogicalGetData key
            member __.LogicalSetData (key, value) =
                System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(key, value)
        }
#endif
         
    [<Test>]
    member __.``Check that worker thread is cleaned up``() = 
        let result = ref null
        let workerThread = ref null
        (
          use worker = new WorkerThread()
          worker.SetWorker()
          async {
              do! worker.SwitchToWorker()
              result := "test"
              workerThread := System.Threading.Thread.CurrentThread
          } |> Async.RunSynchronously
          test <@ !result = "test" @>
          let thread = !workerThread
          test <@ thread <> null @>
          test <@ thread.IsAlive @>
          test <@ thread.IsBackground @>
        )
        
        let thread = !workerThread
        test <@ thread <> null @>
        test <@ not thread.IsAlive @>
        