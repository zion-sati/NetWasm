// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Zeroes a frame range whose size the compiler knows. Wasmtime runs memory.fill out of line, which costs more than a
// few stores, so ranges up to MaximumStoredBytes are zeroed with stores instead: sixteen 16-byte stores at most (a
// Matrix4x4 local alone is 64 bytes), each addressed from the range start through its memarg offset.
internal static class FrameRangeZeroing
{
    public const int MaximumStoredBytes = 256;

    public static void Emit(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        Action<IWasmInstructionWriter> start,
        int size)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(start);
        if (size > MaximumStoredBytes)
        {
            start(code);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            ManagedMemoryEmitter.EmitAddressConstant(code, target, size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
            return;
        }
        for (var written = 0; written < size;)
        {
            var width = Width(size - written);
            start(code);
            if (width == 16)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.FromBytes([(byte)WasmSimdOpcodes.V128Constant, .. new byte[16]])));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedTriple(WasmSimdOpcodes.V128Store, 0, (uint)written)));
            }
            else
            {
                code.Write(width == 8
                    ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0))
                    : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                ManagedMemoryEmitter.EmitStoreBySize(code, target, written, width);
            }
            written += width;
        }
    }

    // The instructions that store or fill a range of this size: one per stored chunk, or the fill.
    public static int Writes(int size)
    {
        if (size > MaximumStoredBytes) return 1;
        var writes = 0;
        for (var written = 0; written < size; written += Width(size - written)) writes++;
        return writes;
    }

    private static int Width(int remaining) => remaining switch
    {
        >= 16 => 16,
        >= 8 => 8,
        >= 4 => 4,
        >= 2 => 2,
        _ => 1,
    };
}
