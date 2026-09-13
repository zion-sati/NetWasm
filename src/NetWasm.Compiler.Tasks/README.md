# NetWasm.Compiler.Tasks

`NetWasm.Compiler.Tasks` supplies the MSBuild tasks that compile NetWasm
projects and write their compiler artifact manifests. It is consumed
transitively by `NetWasm.Sdk`; application projects normally select it through
the SDK rather than referencing the task package directly.
