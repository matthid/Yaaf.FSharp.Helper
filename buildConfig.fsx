// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------

(*
    This file handles the configuration of the Yaaf.AdvancedBuilding build script.

    The first step is handled in build.sh and build.cmd by restoring either paket dependencies or bootstrapping a NuGet.exe and 
    executing NuGet to resolve all build dependencies (dependencies required for the build to work, for example FAKE).

    The secound step is executing build.fsx which loads this file (for configuration), builds the solution and executes all unit tests.
*)

#if FAKE
#else
// Support when file is opened in Visual Studio
#load "packages/Yaaf.AdvancedBuilding/content/buildConfigDef.fsx"
#endif

open BuildConfigDef
open System.Collections.Generic
open System.IO

open Fake
open Fake.Git
open Fake.FSharpFormatting
open AssemblyInfoFile

if isMono then
    monoArguments <- "--runtime=v4.0 --debug"

let buildConfig =
 // Read release notes document
 let release = ReleaseNotesHelper.parseReleaseNotes (File.ReadLines "doc/ReleaseNotes.md")
 { BuildConfiguration.Defaults with
    ProjectName = "Yaaf.FSharp.Helper"
    CopyrightNotice = "Yaaf.FSharp.Helper Copyright © Matthias Dittrich 2015"
    ProjectSummary = "A helper library with some F# functions."
    ProjectDescription = "A helper library with some F# functions."
    ProjectAuthors = ["Matthias Dittrich"]
    NugetTags =  "fsharp helper compiler host"
    PageAuthor = "Matthias Dittrich"
    GithubUser = "matthid"
    Version = release.NugetVersion
    NugetPackages =
      [ "Yaaf.FSharp.Helper.nuspec", (fun config p ->
          { p with
              Version = config.Version
              ReleaseNotes = toLines release.Notes
              Dependencies = 
                [ "FSharp.Core", "3.1.2.1"
                  "FSharpx.Collections", "1.10.1"
                  "Yaaf.Portable.Text.Encoding", "0.7.0"
                  "Yaaf.Logging", "1.0.1" ] }) ]
    UseNuget = false
    SetAssemblyFileVersions = (fun config ->
      let info =
        [ Attribute.Company config.ProjectName
          Attribute.Product config.ProjectName
          Attribute.Copyright config.CopyrightNotice
          Attribute.Version config.Version
          Attribute.FileVersion config.Version
          Attribute.InformationalVersion config.Version]
      CreateFSharpAssemblyInfo "./src/SharedAssemblyInfo.fs" info)
    EnableProjectFileCreation = false
    BuildTargets =
     [ { BuildParams.WithSolution with
          // The default build
          PlatformName = "Profile111"
          // Workaround FSharp.Compiler.Service not liking to have a FSharp.Core here: https://github.com/fsprojects/FSharpx.Reflection/issues/1
          AfterBuild = fun _ -> File.Delete "build/profile111/FSharp.Core.dll"
          SimpleBuildName = "profile111"
          FindUnitTestDlls =
            // Don't run on mono.
            if isMono then (fun _ -> Seq.empty) else BuildParams.Empty.FindUnitTestDlls }
       { BuildParams.WithSolution with
          // The default build
          PlatformName = "Net45"
          // Workaround FSharp.Compiler.Service not liking to have a FSharp.Core here: https://github.com/fsprojects/FSharpx.Reflection/issues/1
          AfterBuild = fun _ -> File.Delete "build/net45/FSharp.Core.dll"
          SimpleBuildName = "net45" } ]
  }