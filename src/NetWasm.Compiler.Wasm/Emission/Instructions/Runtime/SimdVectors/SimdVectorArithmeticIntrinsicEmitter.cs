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

// Add, Subtract, Multiply, MultiplyAddEstimate, Divide, Sqrt, Floor, Ceiling, Truncate, Round, Negate, Abs,
// Min and Max.
internal sealed class SimdVectorArithmeticIntrinsicEmitter(
    ISimdVectorLaneGuard lanes,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        switch (request.Intrinsic)
        {
            case RuntimeIntrinsic.SimdDivide when lane.IsFloating:
                Load(request, code, 0);
                if (ByScalar(request))
                {
                    Get(code, request.Local(1, request.Method.Signature.ParameterTypes[1]));
                    Simd(code, Splat(lane));
                }
                else
                {
                    Load(request, code, 1);
                }
                Simd(code, lane.Shape == SimdLaneShape.F32 ? F32x4Divide : F64x2Divide);
                break;
            case RuntimeIntrinsic.SimdDivide:
                EmitDivisionChecks(request, code, lane);
                // No integer vector division: lane by lane, after the checks.
                PerLane(code, lane, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    Divisor(request, code, lane, index);
                    code.Write(WasmInstruction.NoOperand((lane.Shape == SimdLaneShape.I64, lane.Unsigned) switch
                    {
                        (true, true) => WasmOpcodes.I64DivideUnsigned,
                        (true, false) => WasmOpcodes.I64DivideSigned,
                        (false, true) => WasmOpcodes.I32DivideUnsigned,
                        _ => WasmOpcodes.I32DivideSigned,
                    }));
                });
                break;
            case RuntimeIntrinsic.SimdSquareRoot when lane.IsFloating:
                Load(request, code, 0);
                Simd(code, lane.Shape == SimdLaneShape.F32 ? F32x4SquareRoot : F64x2SquareRoot);
                break;
            case RuntimeIntrinsic.SimdSquareRoot:
                PerLane(code, lane, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    IntegerSquareRoot(code, lane);
                });
                break;
            case RuntimeIntrinsic.SimdFloor or RuntimeIntrinsic.SimdCeiling or
                RuntimeIntrinsic.SimdTruncate or RuntimeIntrinsic.SimdRound:
                // Integer lanes are whole already, and upstream returns them as
                // they are.
                Load(request, code, 0);
                if (lane.IsFloating)
                {
                    Simd(code, (request.Intrinsic, lane.Shape) switch
                    {
                        (RuntimeIntrinsic.SimdFloor, SimdLaneShape.F32) => F32x4Floor,
                        (RuntimeIntrinsic.SimdFloor, _) => F64x2Floor,
                        (RuntimeIntrinsic.SimdCeiling, SimdLaneShape.F32) => F32x4Ceiling,
                        (RuntimeIntrinsic.SimdCeiling, _) => F64x2Ceiling,
                        (RuntimeIntrinsic.SimdTruncate, SimdLaneShape.F32) => F32x4Truncate,
                        (RuntimeIntrinsic.SimdTruncate, _) => F64x2Truncate,
                        // Midpoints round to even, as Math.Round does by default.
                        (_, SimdLaneShape.F32) => F32x4Nearest,
                        _ => F64x2Nearest,
                    });
                }
                break;
            case RuntimeIntrinsic.SimdNegate:
                Load(request, code, 0);
                Simd(code, lane.Shape switch
                {
                    SimdLaneShape.I8 => I8x16Negate,
                    SimdLaneShape.I16 => I16x8Negate,
                    SimdLaneShape.I32 => I32x4Negate,
                    SimdLaneShape.I64 => I64x2Negate,
                    SimdLaneShape.F32 => F32x4Negate,
                    _ => F64x2Negate,
                });
                break;
            case RuntimeIntrinsic.SimdAbsolute:
                Load(request, code, 0);
                if (!lane.Unsigned)
                {
                    // Integer lanes wrap at MinValue, as upstream's Scalar<T>.Abs does.
                    Simd(code, lane.Shape switch
                    {
                        SimdLaneShape.I8 => I8x16Absolute,
                        SimdLaneShape.I16 => I16x8Absolute,
                        SimdLaneShape.I32 => I32x4Absolute,
                        SimdLaneShape.I64 => I64x2Absolute,
                        SimdLaneShape.F32 => F32x4Absolute,
                        _ => F64x2Absolute,
                    });
                }
                break;
            case RuntimeIntrinsic.SimdMinimum or RuntimeIntrinsic.SimdMaximum
                when lane.Shape == SimdLaneShape.I64:
                // No 64-bit integer minimum or maximum: select by comparison.
                Load(request, code, 0);
                Load(request, code, 1);
                Compare(
                    request,
                    code,
                    lane,
                    request.Intrinsic == RuntimeIntrinsic.SimdMaximum
                        ? SimdComparison.GreaterThan
                        : SimdComparison.LessThan);
                Simd(code, V128BitSelect);
                break;
            case RuntimeIntrinsic.SimdMultiply:
                Multiply(request, code, lane);
                break;
            case RuntimeIntrinsic.SimdMultiplyAddEstimate:
                // Wasm SIMD has no fused multiply-add, so the product rounds before
                // the addend is added, as upstream's estimate does on hardware
                // without FMA and as Mono computes it.
                Multiply(request, code, lane);
                Load(request, code, 2);
                Simd(code, Binary(RuntimeIntrinsic.SimdAdd, lane));
                break;
            default:
                // Add, Subtract and the other Minimum and Maximum lanes.
                Load(request, code, 0);
                Load(request, code, 1);
                Simd(code, Binary(request.Intrinsic, lane));
                break;
        }
        Set(code, request.Local(0, CliValueKind.V128));
    }

    // The product of the first two operands, either of which may be a scalar
    // that every lane multiplies by.
    private static void Multiply(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, SimdLane lane)
    {
        Operand(request, code, 0, lane);
        Operand(request, code, 1, lane);
        if (lane.Shape != SimdLaneShape.I8)
        {
            Simd(code, Binary(RuntimeIntrinsic.SimdMultiply, lane));
            return;
        }
        // No 8-bit multiply: the low byte of each widened product.
        Simd(code, I16x8ExtendMultiplyLowI8x16Unsigned);
        Operand(request, code, 0, lane);
        Operand(request, code, 1, lane);
        Simd(code, I16x8ExtendMultiplyHighI8x16Unsigned);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)I8x16Shuffle, .. EvenBytes])));
    }

    // SimdLane by lane, as upstream's Scalar<T>.Divide: a zero divisor throws, and
    // so does MinValue / -1 in 32-bit and 64-bit signed lanes, whose quotient
    // overflows. Narrower lanes divide as int and keep the low bits. The checks
    // all precede the division, which has no other effect, so the first lane
    // that fails decides the exception as it does upstream.
    private void EmitDivisionChecks(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane)
    {
        var wide = lane.Shape == SimdLaneShape.I64;
        var overflows = !lane.Unsigned && lane.Shape is SimdLaneShape.I32 or SimdLaneShape.I64;
        for (var index = 0; index < Lanes(lane); index++)
        {
            if (index == 0 || !ByScalar(request))
            {
                Divisor(request, code, lane, index);
                code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
                Throw(request, code, ManagedExceptionKind.DivideByZero);
            }
            if (overflows)
            {
                LoadLane(request, code, 0, lane, index);
                Constant(code, wide, wide ? long.MinValue : int.MinValue);
                code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
                Divisor(request, code, lane, index);
                Constant(code, wide, -1);
                code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
                Throw(request, code, ManagedExceptionKind.Overflow);
            }
        }
    }

    private void Throw(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        ManagedExceptionKind kind)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, kind, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // Divide(Vector128<T>, T) and the operator take a scalar divisor.
    private static bool ByScalar(RuntimeIntrinsicEmissionRequest request) =>
        request.Method.Signature.ParameterTypes[1] != CliValueKind.V128;

    // Pushes the divisor of one lane: the lane of the second vector, or the scalar.
    private static void Divisor(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane,
        int index)
    {
        if (ByScalar(request))
        {
            Get(code, request.Local(1, request.Method.Signature.ParameterTypes[1]));
        }
        else
        {
            LoadLane(request, code, 1, lane, index);
        }
    }

    // As upstream's Scalar<T>.Sqrt: the square root of the value as float for
    // 8-bit and 16-bit lanes and as double otherwise, truncated back with
    // saturation, so a negative value's NaN becomes 0.
    private static void IntegerSquareRoot(IWasmInstructionWriter code, SimdLane lane)
    {
        var (convert, root, truncate) = (lane.Shape, lane.Unsigned) switch
        {
            (SimdLaneShape.I8 or SimdLaneShape.I16, false) =>
                (WasmOpcodes.F32ConvertI32Signed, WasmOpcodes.F32SquareRoot, WasmOpcodes.I32TruncateSaturateF32Signed),
            (SimdLaneShape.I8 or SimdLaneShape.I16, true) =>
                (WasmOpcodes.F32ConvertI32Unsigned, WasmOpcodes.F32SquareRoot, WasmOpcodes.I32TruncateSaturateF32Unsigned),
            (SimdLaneShape.I32, false) =>
                (WasmOpcodes.F64ConvertI32Signed, WasmOpcodes.F64SquareRoot, WasmOpcodes.I32TruncateSaturateF64Signed),
            (SimdLaneShape.I32, true) =>
                (WasmOpcodes.F64ConvertI32Unsigned, WasmOpcodes.F64SquareRoot, WasmOpcodes.I32TruncateSaturateF64Unsigned),
            (_, false) =>
                (WasmOpcodes.F64ConvertI64Signed, WasmOpcodes.F64SquareRoot, WasmOpcodes.I64TruncateSaturateF64Signed),
            _ => (WasmOpcodes.F64ConvertI64Unsigned, WasmOpcodes.F64SquareRoot, WasmOpcodes.I64TruncateSaturateF64Unsigned),
        };
        code.Write(WasmInstruction.NoOperand(convert));
        code.Write(WasmInstruction.NoOperand(root));
        Saturate(code, truncate);
    }
}
