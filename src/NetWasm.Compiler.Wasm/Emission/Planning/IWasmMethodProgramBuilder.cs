// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IWasmMethodProgramBuilder
{
    WasmMethodLoweringResult Build(
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> methods,
        IReadOnlyDictionary<string, ManagedMethodBody> constructedMethods,
        BranchPrefixDuplicationPolicy branchPrefixDuplication =
            BranchPrefixDuplicationPolicy.SizeAware,
        DispatcherEliminationPolicy dispatcherElimination =
            DispatcherEliminationPolicy.Disabled,
        IMethodOutlineProvider? outlines = null);
}
