using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

internal static class TestRuntimeInitialization
{
    public static RuntimeInitializationPlan Create(int staticDataEnd) =>
        new(staticDataEnd, StackTraceMethodPlan.Disabled);

    public static RuntimeInitializationPlan Create(
        int staticDataEnd,
        RuntimeImportSelection runtimeImports) =>
        new(staticDataEnd, StackTraceMethodPlan.Disabled, runtimeImports);
}

internal sealed class DisabledStackTraceMethodPlanBuilder : IStackTraceMethodPlanBuilder
{
    public StackTraceMethodPlan Build(
        bool enabled,
        ImmutableArray<EntityKey> directMethods,
        ImmutableArray<string> constructedMethods) =>
        StackTraceMethodPlan.Disabled;
}
