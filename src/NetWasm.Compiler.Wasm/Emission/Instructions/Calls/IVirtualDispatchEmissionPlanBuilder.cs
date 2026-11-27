// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface IVirtualDispatchEmissionPlanBuilder
{
    // Exhaustive: the targets cover every receiver the call can have (type-based dispatch, or a bound the receiver
    // flow could not narrow), so object's own implementation may serve the receivers without one as an unchecked
    // fallback. Otherwise the targets are the receivers the flow found, and any other receiver throws.
    VirtualDispatchEmissionPlan Build(
        MethodInstanceModel declaration,
        ImmutableArray<DispatchTargetModel> targets,
        bool exhaustive);
}
