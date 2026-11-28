// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

internal interface IStructResultScratchPlanner
{
    // The kinds of the locals the method needs to move returned fields, with repetition, in a stable order; empty when
    // neither it nor any managed call it makes returns a value type in results. A call to a member or DynamicInvoke
    // intrinsic makes the calls the member execution plan lists.
    ImmutableArray<CliValueKind> Plan(StructuredMethodHeader header, MemberExecutionPlan? memberExecution = null);

    // The kinds the calls among the instructions need (a filter funclet's, which returns no value type of its own).
    ImmutableArray<CliValueKind> Plan(IEnumerable<CilInstruction> instructions, MemberExecutionPlan? memberExecution = null);
}
