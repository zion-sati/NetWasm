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

    public int? NativeCallbackReadinessAddress { get; init; }

    // Present when exports or host callbacks can re-enter the initializer; type, value-type
    // and static-root registration then runs on the first entry only.
    public int? StateRegistrationGuardAddress { get; init; }

    public int? StackTraceSymbolRegistrationGuardAddress { get; init; }

    // The generated function that holds this initialization; every entry calls it.
    public int? InitializerFunctionIndex { get; init; }

    public ImmutableArray<StackTraceSymbolData> StackTraceSymbols { get; init; } = [];
}
