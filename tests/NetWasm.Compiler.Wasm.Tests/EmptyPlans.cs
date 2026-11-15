// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// Plans for emitters a test runs without finalizable types or module data.
internal static class EmptyFinalizationPlan
{
    private static readonly FinalizationPlan NoFinalization = new(ImmutableHashSet<int>.Empty, false);

    extension(FinalizationPlan)
    {
        public static FinalizationPlan Empty => NoFinalization;
    }
}

internal static class EmptyModuleDataPlan
{
    private static readonly ModuleDataPlan NoModuleData = new(
        ImmutableDictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>.Empty, [], [],
        ImmutableDictionary<string, StaticInitializerGuard>.Empty, 0);

    extension(ModuleDataPlan)
    {
        public static ModuleDataPlan Empty => NoModuleData;
    }
}
