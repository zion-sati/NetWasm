// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationPreparation(
    ComponentBoundaryContract componentContract,
    CompilationEntry entry,
    ImmutableArray<ProgramExport> exports,
    ReachabilityRoots reachabilityRoots,
    EntityKey? entryPointArgumentFactory,
    EntityKey? entryPointExit,
    WasmOptimizationSettings optimizationSettings,
    PlatformWorldCapabilities world)
{
    public ComponentBoundaryContract ComponentContract { get; } = componentContract ??
        throw new ArgumentNullException(nameof(componentContract));

    public CompilationEntry Entry { get; } = entry ??
        throw new ArgumentNullException(nameof(entry));

    public ImmutableArray<ProgramExport> Exports { get; } = exports;

    public ReachabilityRoots ReachabilityRoots { get; } = reachabilityRoots ??
        throw new ArgumentNullException(nameof(reachabilityRoots));

    public EntityKey? EntryPointArgumentFactory { get; } = entryPointArgumentFactory;
    public EntityKey? EntryPointExit { get; } = entryPointExit;

    public WasmOptimizationSettings OptimizationSettings { get; } =
        optimizationSettings ?? throw new ArgumentNullException(nameof(optimizationSettings));

    // What the selected world and host give CoreLib's world-dependent composition.
    public PlatformWorldCapabilities World { get; } = world ?? throw new ArgumentNullException(nameof(world));

    // Methods nothing is inlined into, since their functions came out over an engine limit in an earlier
    // compile of the program.
    public ImmutableHashSet<EntityKey> InliningExcludedMethods { get; init; } = [];
}
