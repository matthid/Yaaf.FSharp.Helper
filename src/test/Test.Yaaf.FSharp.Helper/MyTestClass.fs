// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.TestHelper

open System
open System.Collections.Generic
open NUnit.Framework
open Yaaf.Helper
open Yaaf.Logging

[<AutoOpen>]
module TestModule =
    let waitTaskIT (taskId : string) (time : int) (t : System.Threading.Tasks.Task<_>) = 
        try 
            let safeTimeout = 
                if (System.Diagnostics.Debugger.IsAttached) then System.Threading.Timeout.Infinite
                else time
        
            let success = t.Wait(safeTimeout)
            if not success then 
                let span = System.TimeSpan.FromMilliseconds(float time)
                let msg = sprintf "Task (%s) did not finish within the given timeout (%A)!" taskId span
                Log.Err(fun () -> msg)
                Assert.Fail(msg)
        with :? AggregateException as agg -> 
            let agg = agg.Flatten()
            for i in 0..agg.InnerExceptions.Count - 1 do
                Log.Err(fun () -> L "Exception in Task: %O" (agg.InnerExceptions.Item i))
            reraisePreserveStackTrace <| if agg.InnerExceptions.Count = 1 then agg.InnerExceptions.Item 0
                                         else agg :> exn
        t.Result

    let mutable defaultTimeout = 
        if isMono then 1000 * 50 * 1 // 50 secs
        else 1000 * 10 // 10 secs

    let waitTaskT (time : int) (t : System.Threading.Tasks.Task<_>) = waitTaskIT "unknown" time t
    let waitTask (t : System.Threading.Tasks.Task<_>) = waitTaskT defaultTimeout t
    let waitTaskI (taskId : string) (t : System.Threading.Tasks.Task<_>) = waitTaskIT taskId defaultTimeout t
    let private warnMessages = new System.Collections.Generic.Dictionary<Object, String list>()
    let mutable warnedContextNull = false

    let getKey (context : TestContext) = 
        if context = null then "key is null" // :> obj
        else context.Test.FullName

    let warn msg = 
        let key = getKey TestContext.CurrentContext
        match warnMessages.TryGetValue(key) with
        | false, _ -> warnMessages.Add(key, [ msg ])
        | true, l -> warnMessages.[key] <- msg :: l
        if (TestContext.CurrentContext = null && not warnedContextNull) then 
            warnedContextNull <- true
            warnMessages.Add(getKey null, [ "TestContext.CurrentContext is null so we can not seperate warnings by test-case!" ])

    let getWarnings() = 
        match warnMessages.TryGetValue(getKey TestContext.CurrentContext) with
        | false, _ -> None
        | true, warnings -> 
            warnings
            |> List.fold (fun s warning -> sprintf "%s\n%s" warning s) ""
            |> Some

    let warnStop msg = 
        let warnings = 
            match getWarnings() with
            | None -> msg
            | Some other -> sprintf "%s\n%s" other msg
        Assert.Inconclusive(warnings)

    let warnTearDown() = 
        match getWarnings() with
        | None -> ()
        | Some allWarnings -> 
            if (TestContext.CurrentContext <> null && TestContext.CurrentContext.Result.Status = TestStatus.Failed) then Assert.Fail(allWarnings)
            else Assert.Inconclusive(allWarnings)

open TestModule
type SourceLevels = System.Diagnostics.SourceLevels
//[<TestFixture>]
type MyTestClass() = 
    static do System.IO.Directory.CreateDirectory("logs") |> ignore
    static let level_verb = SourceLevels.Verbose
    static let level_all = SourceLevels.All
    static let level_warn = SourceLevels.Warning
    static let level_info = SourceLevels.Information
    
    static let prepare level (logger : System.Diagnostics.TraceListener) = 
        logger.Filter <- new System.Diagnostics.EventTypeFilter(level)
        logger.TraceOutputOptions <- System.Diagnostics.TraceOptions.None
        logger // :> System.Diagnostics.TraceListener 
    
    static let xmlWriter = new SimpleXmlWriterTraceListener("logs/tests.svclog") |> prepare level_all
    
    //new Yaaf.Logging.XmlWriterTraceListener("logs/tests.svclog") |> prepare level_all
    static let listeners : System.Diagnostics.TraceListener [] = 
        [| if not isMono then yield Log.ConsoleLogger level_all |> prepare level_all
           yield xmlWriter |]
    
    let mutable sources : IExtendedTraceSource [] = null
    let mutable testActivity = Unchecked.defaultof<ITracer>
    
    static let shortenTestName (testName:string) (fullName:string) =
        // Because tests have a name like Test.Namespace.Test-Namespace-UnitUnderTests, we want to shorten that
        // Another reason is because we trigger the 260 character limit on windows...
        assert (fullName.EndsWith ("." + testName))
        let firstPart = fullName.Substring(0, fullName.Length - (testName.Length + 1))
        let namespaces = firstPart.Split([|'.'; '+'|])
        assert (namespaces.Length > 1)
        let className = namespaces.[namespaces.Length - 1]
        let prefixName = System.String.Join("-", namespaces |> Seq.take (namespaces.Length - 1))
        let newClassName =
            if className.StartsWith(prefixName) then
                // we can sorten the namespace away and effectively only take the className
                let indexOfColon = className.IndexOf(':')
                if indexOfColon > prefixName.Length - 1 then
                    // Shorten even more by leaving out everything after ':'
                    className.Substring(0, indexOfColon)
                else 
                    className
            else
                // Doesn't have the new Test format (this will be marked obsolete and we will throw here to enforce the new scheme!)
                System.String.Join("-", namespaces)
        
        let newTestName =
            let colonIndex = testName.IndexOf(":")
            let periodIndex = testName.IndexOf(".")
            if colonIndex > -1 then
                // Check if we have 'Test-Yaaf-Class: desc. test dec' format
                let first = testName.Substring(0, colonIndex)
                if not <| first.Contains(" ") && colonIndex < periodIndex then
                    // we assume this format, so we take everything after the period
                    testName.Substring(periodIndex + 1)
                else
                    testName
            else
                testName

        let shortName = sprintf "%s: %s" newClassName newTestName
        assert (shortName.StartsWith("Test-Yaaf-") || shortName.StartsWith("Yaaf-"))
        if shortName.StartsWith("Test-Yaaf-") then
            shortName.Substring("Test-Yaaf".Length + 1)
        else
            shortName.Substring("Yaaf".Length + 1)
            


    static member ShortenTestName testName fullName = shortenTestName testName fullName
    [<TearDownAttribute>] abstract TearDown : unit -> unit
    
    [<TearDownAttribute>]
    override x.TearDown() = 
        warnTearDown()
        testActivity.Dispose()
        testActivity <- Unchecked.defaultof<_>
        sources |> Seq.iter (fun s -> 
                       s.Wrapped.Listeners
                       |> Seq.cast
                       |> Seq.iter (fun (l : System.Diagnostics.TraceListener) -> l.Dispose() |> ignore))
    
    //MyTestClass.Listeners |> 
    [<SetUpAttribute>] abstract Setup : unit -> unit
    
    abstract LogSourceNames : string seq
    default x.LogSourceNames = [] :> _

    [<SetUpAttribute>]
    override x.Setup() = 
        // Setup logging
        let test = TestContext.CurrentContext.Test
        let cache = System.Diagnostics.TraceEventCache()
        let shortName = shortenTestName test.Name test.FullName
        let name = CopyListenerHelper.cleanName shortName
        
        let testListeners = 
            listeners 
            |> Array.map (Yaaf.Logging.CopyListenerHelper.duplicateListener "Yaaf.TestHelper" cache name)
        
        let addLogging (sourceName : String) = 
            sourceName
            |> Log.SetupNamespace (fun source ->
                match source with
                | :? IExtendedTraceSource as ext ->
                    ext.Wrapped.Switch.Level <- System.Diagnostics.SourceLevels.All
                    if listeners.Length > 0 then 
                        ext.Wrapped.Listeners.Clear()
                        //if not <| source.Listeners.Contains(MyTestClass.Listeners.[0]) then
                    ext.Wrapped.Listeners.AddRange(testListeners)
                    ext
                | _ -> failwith "unknown tracesource!")
        
        let defSource = addLogging "Yaaf.Logging"
        Log.SetUnhandledSource defSource
        
        sources <- x.LogSourceNames 
                |> Seq.map addLogging 
                |> Seq.append [ defSource ] 
                |> Seq.toArray
        testActivity <- Log.StartActivity(shortName)
        ()
    


    