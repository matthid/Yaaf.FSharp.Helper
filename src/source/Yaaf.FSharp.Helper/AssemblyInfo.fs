namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Yaaf.FSharpHelper")>]
[<assembly: AssemblyProductAttribute("Yaaf.FSharpHelper")>]
[<assembly: AssemblyDescriptionAttribute("A set of F# functions I missed in other packages.")>]
[<assembly: AssemblyVersionAttribute("1.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0"
