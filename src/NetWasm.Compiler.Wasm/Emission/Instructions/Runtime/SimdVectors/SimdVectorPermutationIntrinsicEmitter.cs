using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdByteShuffles;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdLaneOpcodes;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdVectorCode;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// Shuffle by lane indices, and the fixed permutations.
internal sealed class SimdVectorPermutationIntrinsicEmitter(
    ISimdVectorLaneGuard lanes) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        if (request.Intrinsic == RuntimeIntrinsic.SimdShuffle)
        {
            Load(request, code, 0);
            ShuffleByteIndices(request, code, lane);
            Simd(code, I8x16Swizzle);
        }
        else
        {
            // Permute: ZipLower, UnzipEven, ConcatUpperLower and the rest.
            Load(request, code, 0);
            Load(request, code, 1);
            Shuffle(code, PermutedBytes(request.Method.Definition.Name, LaneSize(lane)));
        }
        Set(code, request.Local(0, CliValueKind.V128));
    }

    // The byte indices of a shuffle: each lane's index times its size plus the
    // lane's byte offsets. An index past the last lane becomes byte indices of 16
    // or more, which i8x16.swizzle reads as zero, as upstream's Shuffle does.
    private static void ShuffleByteIndices(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, SimdLane lane)
    {
        var size = LaneSize(lane);
        Load(request, code, 1);
        if (size == 1)
        {
            return;
        }
        if (size == 8)
        {
            // No unsigned 64-bit minimum: the index's low bit selects the lane, and
            // 0x80 marks every byte of a lane whose index is 2 or more.
            VectorConstant(code, Repeat(8, 1));
            Simd(code, V128And);
            VectorConstant(code, Repeat(8, 0x0808080808080808));
            Simd(code, I64x2Multiply);
            VectorConstant(code, Repeat(8, unchecked((long)0x8080808080808080)));
            Load(request, code, 1);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
            Simd(code, I64x2ShiftRightUnsigned);
            VectorConstant(code, new byte[16]);
            Simd(code, I64x2Equal);
            Simd(code, V128AndNot);
            Simd(code, V128Or);
        }
        else
        {
            // The index clamped to the lane count, times the size, in every byte.
            var lanes = 16 / size;
            VectorConstant(code, Repeat(size, lanes));
            Simd(code, size == 2 ? I16x8MinimumUnsigned : I32x4MinimumUnsigned);
            VectorConstant(code, Repeat(size, size == 2 ? 0x0202 : 0x04040404));
            Simd(code, size == 2 ? I16x8Multiply : I32x4Multiply);
        }
        VectorConstant(code, [.. Enumerable.Range(0, 16).Select(index => (byte)(index % size))]);
        Simd(code, I8x16Add);
    }
}
