// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopContinuationBuilder
{
    StructuredSequenceDraft Build(ControlFlowStructuringState state, int offset, int length, LoopRegion loop);
}
