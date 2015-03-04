// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Test.Yaaf.TestHelper

open Yaaf.TestHelper
open NUnit.Framework
open Swensen.Unquote

[<TestFixture>]
type ``Test-Yaaf-TestHelper-MyTestClass: Check that MyTestClass works``() = 
    inherit MyTestClass()
    
    [<Test>]
    member x.``Check TestClass Setup and TearDown``() = ()

    [<Test>]
    member x.``Check Testnames are properly shortened (1)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "Yaaf-Xmpp-MessageArchiveManager: Some interface tests.Check that storing and deleting default preferences works"
                "Test.Yaaf.Xmpp.MessageArchiveManager.Sql.Test-Yaaf-Xmpp-MessageArchiveManager-Sql-DbContext.Yaaf-Xmpp-MessageArchiveManager: Some interface tests.Check that storing and deleting default preferences works"

        test <@ result = "Xmpp-MessageArchiveManager-Sql-DbContext: Check that storing and deleting default preferences works" @>

    [<Test>]
    member x.``Check Testnames are properly shortened (2)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "Check that guardAsync guards Yaaf.Xmpp.XmlException"
                "Test.Yaaf.Xmpp.TestHelper+SimpleHelperMethodTests.Check that guardAsync guards Yaaf.Xmpp.XmlException"


        test <@ result = "Xmpp-TestHelper-SimpleHelperMethodTests: Check that guardAsync guards Yaaf.Xmpp.XmlException" @>

    [<Test>]
    member x.``Check Testnames are properly shortened (3)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "Check that created jid bind element has the right type"
                "Test.Yaaf.Xmpp.Test-Yaaf-Xmpp-Runtime-Features-BindFeature: Check that Parsing works.Check that created jid bind element has the right type"


        test <@ result = "Xmpp-Runtime-Features-BindFeature: Check that created jid bind element has the right type" @>

    [<Test>]
    member x.``Check Testnames are properly shortened (4)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "Check if we can send a simple iq stanza"
                "Test.Yaaf.Xmpp.Test-Yaaf-Xmpp-XmppClient: Check if XmppClient handles the Runtime properly.Check if we can send a simple iq stanza"


        test <@ result = "Xmpp-XmppClient: Check if we can send a simple iq stanza" @>

        
    [<Test>]
    member x.``Check Testnames are properly shortened (5)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "check that XElement can be converted to openinfo"
                "Test.Yaaf.Xmpp.Runtime.TestStreamOpening.check that XElement can be converted to openinfo"


        test <@ result = "Xmpp-Runtime-TestStreamOpening: check that XElement can be converted to openinfo" @>

   
    [<Test>]
    member x.``Check Testnames are properly shortened (6)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "Yaaf-Xmpp-MessageArchiveManager: Some interface tests.Check that storing and deleting default preferences works"
                "Test.Yaaf.Xmpp.MessageArchiveManager.Sql.Test-Yaaf-Xmpp-MessageArchiveManager-Sql-DbContext: some additional tests.Yaaf-Xmpp-MessageArchiveManager: Some interface tests.Check that storing and deleting default preferences works"

        test <@ result = "Xmpp-MessageArchiveManager-Sql-DbContext: Check that storing and deleting default preferences works" @>

        
        
    [<Test>]
    member x.``Check Testnames are properly shortened (7)``() = 
        let result = 
            MyTestClass.ShortenTestName 
                "check that XElement can be converted to openinfo"
                "Yaaf.Xmpp.Runtime.TestStreamOpening.check that XElement can be converted to openinfo"


        test <@ result = "Xmpp-Runtime-TestStreamOpening: check that XElement can be converted to openinfo" @>