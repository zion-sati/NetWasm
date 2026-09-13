using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed record ManagedExecutableComponentAdapterRequest(
    string OutputPath,
    ComponentTarget Target,
    ManagedExecutableEntryPointAbi EntryPoint);
