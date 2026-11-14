# NetWasm.App.Runtime

`NetWasm.App.Runtime` carries the implementations of NetWasm.App, the framework libraries of the
`netwasm0.1` profile: System.Collections, System.Linq, System.Text.Json, System.Xml, System.Net.Http and the
rest of the assemblies the .NET shared framework places outside System.Private.CoreLib. They are IL, so one
package serves wasm32 and wasm64.

The assemblies are under `framework/`, outside NuGet's asset folders: C# compiles against
`NetWasm.App.Ref`, and this package's `buildTransitive` targets list the implementations for the NetWasm
compiler, which compiles the ones a program reaches into its module.

The NetWasm SDK references this package implicitly for a `netwasm0.1` build.
