// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Each field moves as a field load or store of its type and size does, so a returned value reads back as the memory
// it came from.
internal sealed class StructResultEmitter(ITargetLayout layouts) : IStructResultEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));

    public void Emit(IWasmInstructionWriter code, StructResultShape shape, Action<IWasmInstructionWriter> emitAddress,
        VectorScratch? scratch = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(emitAddress);
        foreach (var field in shape.Fields)
        {
            emitAddress(code);
            ManagedMemoryEmitter.EmitLoadByType(code, _layouts.Target, field.Offset, field.Type, field.Size, scratch);
        }
    }

    public void Emit(IWasmInstructionWriter code, StructResultShape shape, ImmutableArray<int> locals,
        Action<IWasmInstructionWriter> emitAddress, VectorScratch? scratch = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(emitAddress);
        if (locals.Length != shape.Fields.Length)
            throw new ArgumentException("One local per returned field is required.", nameof(locals));
        for (var index = locals.Length - 1; index >= 0; index--)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)locals[index])));
        }
        for (var index = 0; index < locals.Length; index++)
        {
            var field = shape.Fields[index];
            emitAddress(code);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)locals[index])));
            ManagedMemoryEmitter.EmitStoreByType(code, _layouts.Target, field.Offset, field.Type, field.Size, scratch);
        }
        StructPaddingEmitter.Emit(code, _layouts.Target, shape, emitAddress);
    }
}
