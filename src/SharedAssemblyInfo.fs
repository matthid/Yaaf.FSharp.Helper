﻿namespace System
open System.Reflection

[<assembly: AssemblyCompanyAttribute("Yaaf.FSharp.Helper")>]
[<assembly: AssemblyProductAttribute("Yaaf.FSharp.Helper")>]
[<assembly: AssemblyCopyrightAttribute("Yaaf.FSharp.Helper Copyright © Matthias Dittrich 2015")>]
[<assembly: AssemblyVersionAttribute("0.1.0")>]
[<assembly: AssemblyFileVersionAttribute("0.1.0")>]
[<assembly: AssemblyInformationalVersionAttribute("0.1.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.0"
