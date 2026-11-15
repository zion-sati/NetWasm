// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IConstantComparisonEvaluator
{
    // Arguments include the receiver of an instance method.
    ScalarConstant? Evaluate(MethodInstanceModel method, IReadOnlyList<ScalarConstant> arguments);
}
