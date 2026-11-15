// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IManagedMethodOptimizerFactory
{
    IManagedMethodOptimizer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        ICalledMethodResolver calledMethods,
        IRuntimeIntrinsicRegistry intrinsics,
        IMethodSpecializer constantCallFolding,
        IDispatchTargetResolver dispatchTargets,
        IFinalizerResolver finalizers,
        ITypeDefinitionResolver typeDefinitions,
        Loops.IArrayLoopMethodResolver arrayLoopMethods,
        Loops.ISpanLengthResolver spanLengths,
        Loops.IVectorLoopMethodResolver vectorLoopMethods,
        WasmOptimizationSettings settings,
        // Methods nothing is inlined into, since their functions came out over an engine limit.
        ImmutableHashSet<EntityKey>? inliningExcludedCallers = null);
}
