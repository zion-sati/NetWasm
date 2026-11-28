// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// A value stored field by field (returned in Wasm results into memory, or passed in its fields to a method that keeps
// it in its frame) writes its padding as zeros. A whole copy carries every byte, and a value's padding is zero wherever
// it came from zeroed memory (a new object or array, a zero-initialized local), so desktop .NET's copy writes zeros
// there; storing the fields alone leaves the bytes the memory held, such as the address a frame there held before, and
// a program that writes the value's bytes out (MemoryPack serializing an int?) shows them.
internal static class StructPaddingEmitter
{
    public static void Emit(IWasmInstructionWriter code, WasmTargetLayout target, StructResultShape shape,
        Action<IWasmInstructionWriter> emitAddress)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(emitAddress);
        foreach (var (offset, length) in shape.Padding())
        {
            for (var at = offset; at < offset + length;)
            {
                var size = Chunk(offset + length - at);
                emitAddress(code);
                code.Write(size == 8
                    ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0))
                    : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                ManagedMemoryEmitter.EmitStoreBySize(code, target, at, size);
                at += size;
            }
        }
    }

    private static int Chunk(int remaining) => remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
}
