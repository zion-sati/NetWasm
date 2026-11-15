// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IDispatcherExitSelector
{
    int[] Select(IEnumerable<int> successors, IEnumerable<int> allowed);
}
