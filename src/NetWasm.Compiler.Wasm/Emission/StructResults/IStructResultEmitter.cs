// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Moves a value returned in results between the stack and memory, field by field.
internal interface IStructResultEmitter
{
    // Pushes the value's fields, in field order, from the address emitAddress pushes.
    void Emit(IWasmInstructionWriter code, StructResultShape shape, Action<IWasmInstructionWriter> emitAddress,
        VectorScratch? scratch = null);

    // Takes the shape's results off the stack into the locals (one per field, in field order), then stores each at its
    // offset from the address emitAddress pushes.
    void Emit(IWasmInstructionWriter code, StructResultShape shape, ImmutableArray<int> locals,
        Action<IWasmInstructionWriter> emitAddress, VectorScratch? scratch = null);
}
