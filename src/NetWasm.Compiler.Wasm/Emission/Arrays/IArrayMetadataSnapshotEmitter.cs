// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayMetadataSnapshotEmitter
{
    void Emit(IWasmInstructionWriter code, MethodEmissionContext context);

    void Emit(IWasmInstructionWriter code, MethodEmissionContext context, ArrayReferenceSlot slot);

    void Emit(IWasmInstructionWriter code, MethodEmissionContext context, StructuredBlockId loopCondition);
}
