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

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// Writes the operands, lanes and constants the lowerings share.
internal static class SimdVectorCode
{
    // Builds a vector of the result's lanes from the scalar each index pushes.
    internal static void PerLane(IWasmInstructionWriter code, SimdLane result, Action<int> push)
    {
        for (var index = 0; index < Lanes(result); index++)
        {
            push(index);
            if (index == 0)
            {
                Simd(code, Splat(result));
            }
            else
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedPair(ReplaceLane(result), (uint)index)));
            }
        }
    }

    // Pushes one lane of a vector argument. Only get_Item has a receiver, and
    // EmitGetElement reads it, so every vector argument here is a vector value.
    internal static void LoadLane(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argument,
        SimdLane lane,
        int index)
    {
        Get(code, request.Local(argument, CliValueKind.V128));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedPair(ExtractLane(lane), (uint)index)));
    }

    internal static void Constant(IWasmInstructionWriter code, bool wide, long value) =>
        code.Write(wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed((int)value)));

    internal static void Saturate(IWasmInstructionWriter code, byte truncate) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(truncate)));

    internal static void VectorConstant(IWasmInstructionWriter code, byte[] bytes) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)V128Constant, .. bytes])));

    internal static void Shuffle(IWasmInstructionWriter code, byte[] lanes) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)I8x16Shuffle, .. lanes])));

    // Pushes the comparison mask of the first two arguments. Wasm compares
    // 64-bit lanes only as signed, so unsigned ones compare with the sign bit
    // flipped.
    internal static void Compare(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane,
        SimdComparison comparison)
    {
        var flip = lane.Shape == SimdLaneShape.I64 && lane.Unsigned && comparison != SimdComparison.Equal;
        foreach (var argument in (ReadOnlySpan<int>)[0, 1])
        {
            Load(request, code, argument);
            if (flip)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.FromBytes([(byte)V128Constant, .. I64SignBits])));
                Simd(code, V128Xor);
            }
        }

        var unsigned = lane.Unsigned && !flip;
        Simd(code, (comparison, lane.Shape, unsigned) switch
        {
            (SimdComparison.Equal, SimdLaneShape.I8, _) => I8x16Equal,
            (SimdComparison.Equal, SimdLaneShape.I16, _) => I16x8Equal,
            (SimdComparison.Equal, SimdLaneShape.I32, _) => I32x4Equal,
            (SimdComparison.Equal, SimdLaneShape.I64, _) => I64x2Equal,
            (SimdComparison.Equal, SimdLaneShape.F32, _) => F32x4Equal,
            (SimdComparison.Equal, SimdLaneShape.F64, _) => F64x2Equal,
            (SimdComparison.LessThan, SimdLaneShape.I8, false) => I8x16LessThanSigned,
            (SimdComparison.LessThan, SimdLaneShape.I8, true) => I8x16LessThanUnsigned,
            (SimdComparison.LessThan, SimdLaneShape.I16, false) => I16x8LessThanSigned,
            (SimdComparison.LessThan, SimdLaneShape.I16, true) => I16x8LessThanUnsigned,
            (SimdComparison.LessThan, SimdLaneShape.I32, false) => I32x4LessThanSigned,
            (SimdComparison.LessThan, SimdLaneShape.I32, true) => I32x4LessThanUnsigned,
            (SimdComparison.LessThan, SimdLaneShape.I64, _) => I64x2LessThanSigned,
            (SimdComparison.LessThan, SimdLaneShape.F32, _) => F32x4LessThan,
            (SimdComparison.LessThan, SimdLaneShape.F64, _) => F64x2LessThan,
            (SimdComparison.GreaterThan, SimdLaneShape.I8, false) => I8x16GreaterThanSigned,
            (SimdComparison.GreaterThan, SimdLaneShape.I8, true) => I8x16GreaterThanUnsigned,
            (SimdComparison.GreaterThan, SimdLaneShape.I16, false) => I16x8GreaterThanSigned,
            (SimdComparison.GreaterThan, SimdLaneShape.I16, true) => I16x8GreaterThanUnsigned,
            (SimdComparison.GreaterThan, SimdLaneShape.I32, false) => I32x4GreaterThanSigned,
            (SimdComparison.GreaterThan, SimdLaneShape.I32, true) => I32x4GreaterThanUnsigned,
            (SimdComparison.GreaterThan, SimdLaneShape.I64, _) => I64x2GreaterThanSigned,
            (SimdComparison.GreaterThan, SimdLaneShape.F32, _) => F32x4GreaterThan,
            (SimdComparison.GreaterThan, SimdLaneShape.F64, _) => F64x2GreaterThan,
            (SimdComparison.LessOrEqual, SimdLaneShape.I8, false) => I8x16LessOrEqualSigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.I8, true) => I8x16LessOrEqualUnsigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.I16, false) => I16x8LessOrEqualSigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.I16, true) => I16x8LessOrEqualUnsigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.I32, false) => I32x4LessOrEqualSigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.I32, true) => I32x4LessOrEqualUnsigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.I64, _) => I64x2LessOrEqualSigned,
            (SimdComparison.LessOrEqual, SimdLaneShape.F32, _) => F32x4LessOrEqual,
            (SimdComparison.LessOrEqual, SimdLaneShape.F64, _) => F64x2LessOrEqual,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I8, false) => I8x16GreaterOrEqualSigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I8, true) => I8x16GreaterOrEqualUnsigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I16, false) => I16x8GreaterOrEqualSigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I16, true) => I16x8GreaterOrEqualUnsigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I32, false) => I32x4GreaterOrEqualSigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I32, true) => I32x4GreaterOrEqualUnsigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.I64, _) => I64x2GreaterOrEqualSigned,
            (SimdComparison.GreaterOrEqual, SimdLaneShape.F32, _) => F32x4GreaterOrEqual,
            _ => F64x2GreaterOrEqual,
        });
    }

    // Pushes the address argument, advanced by its optional nuint element offset.
    internal static void ElementAddress(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argument,
        SimdLane lane)
    {
        var parameters = request.Method.Signature.ParameterTypes;
        Get(code, request.Local(argument, parameters[argument]));
        if (parameters.Length == argument + 1)
        {
            return;
        }

        var size = LaneSize(lane);
        var wide = request.Target.UsesMemory64;
        Get(code, request.Local(argument + 1, CliValueKind.NativeInt));
        code.Write(wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(size))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
    }

    internal static void Load(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argument) =>
        Get(code, request.Local(argument, CliValueKind.V128));

    // A vector operand, or a scalar operand broadcast to every lane.
    internal static void Operand(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int argument, SimdLane lane)
    {
        var type = request.Method.Signature.ParameterTypes[argument];
        if (type == CliValueKind.V128)
        {
            Load(request, code, argument);
            return;
        }
        Get(code, request.Local(argument, type));
        Simd(code, Splat(lane));
    }

    internal static void Simd(IWasmInstructionWriter code, uint opcode) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.Prefixed(opcode)));

    internal static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    internal static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
