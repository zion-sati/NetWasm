// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface ITerminalBranchValueFrameEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context,
        TerminalBranchValueFramePlacement placement,
        Func<StructuredBlockOccurrence, MethodEmissionContext, ConditionValue> emitCondition,
        Action<StructuredSequence, MethodEmissionContext> emitBody);
}
