// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.Methods;

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

internal interface IControlFlowDispatcherEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence, int> emitSequence);
}
