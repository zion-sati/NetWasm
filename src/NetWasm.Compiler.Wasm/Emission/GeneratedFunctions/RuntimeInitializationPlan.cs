using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record ModuleInitializerCall(int GuardAddress, int FunctionIndex);

internal sealed record RuntimeInitializationPlan
{
    public RuntimeInitializationPlan(
        int staticDataEnd,
        StackTraceMethodPlan stackTraceMethods,
        RuntimeImportSelection runtimeImportSelection = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(staticDataEnd);
        ArgumentNullException.ThrowIfNull(stackTraceMethods);
        StaticDataEnd = staticDataEnd;
        StackTraceMethods = stackTraceMethods;
        RuntimeImportSelection = runtimeImportSelection;
    }

    public int StaticDataEnd { get; }

    public StackTraceMethodPlan StackTraceMethods { get; }

    public RuntimeImportSelection RuntimeImportSelection { get; }

    public ImmutableArray<ModuleInitializerCall> ModuleInitializers { get; init; } = [];
}
