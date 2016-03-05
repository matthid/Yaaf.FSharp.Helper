namespace Yaaf.FSharp.Helper

module Lazy =
    let force (l:Lazy<_>) = l.Force()

