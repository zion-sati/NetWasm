// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

    // A component's instantiation calls the state initializer at this index.
    public const int StateInitializerFunctionIndex = 89;

    public static RuntimeInitializationPlan Create(int staticDataEnd) =>
        new(staticDataEnd, StackTraceMethodPlan.Disabled)
        {
            InitializerFunctionIndex = InitializerFunctionIndex,
            StateInitializerFunctionIndex = StateInitializerFunctionIndex,
        };

    public static RuntimeInitializationPlan Create(
        int staticDataEnd,
        RuntimeImportSelection runtimeImports) =>
        new(staticDataEnd, StackTraceMethodPlan.Disabled, runtimeImports)
        {
            InitializerFunctionIndex = InitializerFunctionIndex,
            StateInitializerFunctionIndex = StateInitializerFunctionIndex,
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
