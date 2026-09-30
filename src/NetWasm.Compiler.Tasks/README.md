# NetWasm.Compiler.Tasks

`NetWasm.Compiler.Tasks` supplies the MSBuild tasks that compile NetWasm
projects and write their compiler artifact manifests. It is consumed
transitively by `NetWasm.Sdk`; application projects normally select it through
the SDK rather than referencing the task package directly.

At the default `dotnet build` verbosity, the compiler writes its version,
copyright notice, active compilation stage and elapsed time. Use
`dotnet build -v:q` to suppress informational compiler output. Use
`dotnet build -p:NoLogo=true` to hide the compiler version and copyright
banner while keeping stage updates visible.
