// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface INormalLeaveTargetFinder
{
    int[] Find(ControlFlowStructuringState state, IEnumerable<CilExceptionRegion> regions, IReadOnlyCollection<ExceptionGroupSource> excluded, IReadOnlyCollection<ExceptionGroupSource> handlerExcluded);
}
