// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// One instruction of a block, with how many stack values it consumes and produces and the stack kinds after it.
internal sealed record ManagedAggregateInstructionStep(
    StructuredBlockId Block,
    CilInstruction Instruction,
    int Produced,
    int Consumed,
    ImmutableArray<CliValueKind> OutgoingKinds);
