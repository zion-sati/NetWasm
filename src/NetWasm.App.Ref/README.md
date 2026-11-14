# NetWasm.App.Ref

`NetWasm.App.Ref` is the compile-time contract of NetWasm.App, the framework libraries of the `netwasm0.1`
profile: System.Collections, System.Linq, System.Text.Json, System.Xml, System.Net.Http and the rest of
the assemblies the .NET shared framework places outside System.Private.CoreLib. It contains their reference
assemblies, with metadata and no method bodies, under `ref/NetWasm,Version=v0.1`.

It also carries `netstandard.dll`, generated from the pinned .NET Standard 2.1 contract. The facade
forwards each standard type to the assembly that declares it publicly, NetWasm.CoreLib or a NetWasm.App
library, so packages built for `netstandard2.0` and `netstandard2.1` bind to the framework. It contains type
forwarders only.

Its analyzers are the System.Text.Json and System.Text.RegularExpressions source generators, as
Microsoft.NETCore.App.Ref carries them.

The NetWasm SDK references this package implicitly for a `netwasm0.1` build. The implementations ship in
`NetWasm.App.Runtime`.
