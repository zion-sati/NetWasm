using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

internal static class TestRuntimeInitialization
{
    // Entries call the module's runtime initializer at this index.
    public const int InitializerFunctionIndex = 90;

    public static RuntimeInitializationPlan Create(int staticDataEnd) =>
        new(staticDataEnd, StackTraceMethodPlan.Disabled) { InitializerFunctionIndex = InitializerFunctionIndex };

    public static RuntimeInitializationPlan Create(
        int staticDataEnd,
        RuntimeImportSelection runtimeImports) =>
        new(staticDataEnd, StackTraceMethodPlan.Disabled, runtimeImports)
        {
            InitializerFunctionIndex = InitializerFunctionIndex,
        };
}

internal sealed class DisabledStackTraceMethodPlanBuilder : IStackTraceMethodPlanBuilder
{
    public StackTraceMethodPlan Build(
        bool enabled,
        ImmutableArray<EntityKey> directMethods,
        ImmutableArray<StackTraceConstructedMethod> constructedMethods,
        ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>
            sourceLocations) =>
        StackTraceMethodPlan.Disabled;
}
