// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler.Analysis;

internal interface IMemberExecutionPlanner
{
    MemberExecutionPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<MethodInstanceModel> methodDescriptors,
        IEnumerable<FieldInstanceModel> fieldDescriptors);
}
