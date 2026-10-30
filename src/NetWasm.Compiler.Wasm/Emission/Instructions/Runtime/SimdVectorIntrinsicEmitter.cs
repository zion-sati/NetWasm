using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Lowers the portable Vector128<T> and Vector<T> operations, both 16 bytes in
// NetWasm, to Wasm SIMD. Vector values are Wasm v128 values in the evaluation
// stack's v128 locals; an instance method's receiver is an address the vector
// is read from. Element types the vectors do not support throw, as upstream's
// software implementation does.
internal sealed class SimdVectorIntrinsicEmitter(
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    private enum LaneShape { I8, I16, I32, I64, F32, F64 }

    private readonly record struct Lane(LaneShape Shape, bool Unsigned)
    {
        public bool IsFloating => Shape is LaneShape.F32 or LaneShape.F64;

        // The integer lanes of the same width, for bitwise views of floating lanes.
        public LaneShape Bits => Shape switch
        {
            LaneShape.F32 => LaneShape.I32,
            LaneShape.F64 => LaneShape.I64,
            _ => Shape,
        };
    }

    private enum Comparison { Equal, LessThan, GreaterThan, LessOrEqual, GreaterOrEqual }

    private static readonly byte[] EvenBytes =
        [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30];

    private static readonly byte[] I64SignBits =
        [0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0x80];

    // The low half of each 32-bit and 64-bit lane of two vectors, which
    // truncates them as Narrow does.
    private static readonly byte[] EvenHalfwords =
        [0, 1, 4, 5, 8, 9, 12, 13, 16, 17, 20, 21, 24, 25, 28, 29];

    private static readonly byte[] EvenWords =
        [0, 1, 2, 3, 8, 9, 10, 11, 16, 17, 18, 19, 24, 25, 26, 27];

    // The low two lanes of each of two vectors, and the upper two lanes of one.
    private static readonly byte[] LowWords =
        [0, 1, 2, 3, 4, 5, 6, 7, 16, 17, 18, 19, 20, 21, 22, 23];

    private static readonly byte[] UpperWords =
        [8, 9, 10, 11, 12, 13, 14, 15, 8, 9, 10, 11, 12, 13, 14, 15];

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var signature = request.Method.Signature;
        var vector = !request.Method.Definition.IsStatic ? request.Method.DeclaringType : request.Intrinsic switch
        {
            // A product may take its scalar first.
            RuntimeIntrinsic.SimdCreateBroadcast or RuntimeIntrinsic.SimdCreateElements or
                RuntimeIntrinsic.SimdLoad or RuntimeIntrinsic.SimdMultiply => signature.ReturnSignatureType,
            // Vector.ConditionalSelect(Vector<int>, Vector<float>, Vector<float>)
            // takes its condition with a different element type.
            RuntimeIntrinsic.SimdConditionalSelect => signature.ParameterSignatureTypes[1],
            _ => signature.ParameterSignatureTypes[0],
        };
        var result = default(Lane);
        if (vector.TypeArguments is not [var element] ||
            !TryGetLane(element, request.Target, out var lane) ||
            request.Intrinsic is RuntimeIntrinsic.SimdReinterpret or RuntimeIntrinsic.SimdConvert or
                RuntimeIntrinsic.SimdWidenLower or RuntimeIntrinsic.SimdWidenUpper or RuntimeIntrinsic.SimdNarrow &&
            (signature.ReturnSignatureType.TypeArguments is not [var target] ||
                !TryGetLane(target, request.Target, out result)))
        {
            exceptions.Emit(
                code,
                ManagedExceptionKind.NotSupported,
                request.Instruction.Context.ManagedThrowFunction);
            return;
        }

        switch (request.Intrinsic)
        {
            case RuntimeIntrinsic.SimdEqualsAll:
            case RuntimeIntrinsic.SimdNotEqualsAll:
            case RuntimeIntrinsic.SimdEqualsAny:
                Compare(request, code, lane, Comparison.Equal);
                if (request.Intrinsic == RuntimeIntrinsic.SimdEqualsAny)
                {
                    Simd(code, V128AnyTrue);
                }
                else
                {
                    Simd(code, AllTrue(lane.Bits));
                    if (request.Intrinsic == RuntimeIntrinsic.SimdNotEqualsAll)
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                    }
                }
                Set(code, request.Local(0, CliValueKind.I4));
                return;
            case RuntimeIntrinsic.SimdCompareAll:
            case RuntimeIntrinsic.SimdCompareAny:
                // LessThanAll, GreaterThanOrEqualAny and the rest: every lane, or
                // any lane, of the comparison.
                var name = request.Method.Definition.Name;
                Compare(request, code, lane, name.StartsWith("LessThanOrEqual", StringComparison.Ordinal) ? Comparison.LessOrEqual
                    : name.StartsWith("LessThan", StringComparison.Ordinal) ? Comparison.LessThan
                    : name.StartsWith("GreaterThanOrEqual", StringComparison.Ordinal) ? Comparison.GreaterOrEqual
                    : Comparison.GreaterThan);
                Simd(code, request.Intrinsic == RuntimeIntrinsic.SimdCompareAll ? AllTrue(lane.Bits) : V128AnyTrue);
                Set(code, request.Local(0, CliValueKind.I4));
                return;
            case RuntimeIntrinsic.SimdExtractMostSignificantBits:
                Load(request, code, 0);
                Simd(code, BitMask(lane.Bits));
                Set(code, request.Local(0, CliValueKind.I4));
                return;
            case RuntimeIntrinsic.SimdToScalar:
                Load(request, code, 0);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedPair(ExtractLane(lane), 0)));
                Set(code, request.Local(0, signature.ReturnType));
                return;
            case RuntimeIntrinsic.SimdGetElement:
                EmitGetElement(request, code, element, lane);
                return;
            case RuntimeIntrinsic.SimdSum:
                SumLanes(request, code, lane, 0, Lanes(lane));
                Normalize(code, lane);
                Set(code, request.Local(0, signature.ReturnType));
                return;
            case RuntimeIntrinsic.SimdDivide when !lane.IsFloating:
                EmitDivisionChecks(request, code, lane);
                break;
            case RuntimeIntrinsic.SimdStore:
                ElementAddress(request, code, 1, lane);
                Load(request, code, 0);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedTriple(V128Store, 0, 0)));
                return;
        }

        EmitVector(request, code, lane, result);
        Set(code, request.Local(0, CliValueKind.V128));
    }

    private static void EmitVector(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane,
        Lane result)
    {
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
                Simd(code, lane.Shape == LaneShape.F32 ? F32x4Divide : F64x2Divide);
                return;
            case RuntimeIntrinsic.SimdDivide:
                // No integer vector division: lane by lane, after the checks.
                PerLane(code, lane, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    Divisor(request, code, lane, index);
                    code.Write(WasmInstruction.NoOperand((lane.Shape == LaneShape.I64, lane.Unsigned) switch
                    {
                        (true, true) => WasmOpcodes.I64DivideUnsigned,
                        (true, false) => WasmOpcodes.I64DivideSigned,
                        (false, true) => WasmOpcodes.I32DivideUnsigned,
                        _ => WasmOpcodes.I32DivideSigned,
                    }));
                });
                return;
            case RuntimeIntrinsic.SimdSquareRoot when lane.IsFloating:
                Load(request, code, 0);
                Simd(code, lane.Shape == LaneShape.F32 ? F32x4SquareRoot : F64x2SquareRoot);
                return;
            case RuntimeIntrinsic.SimdSquareRoot:
                PerLane(code, lane, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    IntegerSquareRoot(code, lane);
                });
                return;
            case RuntimeIntrinsic.SimdFloor or RuntimeIntrinsic.SimdCeiling or
                RuntimeIntrinsic.SimdTruncate or RuntimeIntrinsic.SimdRound:
                // Integer lanes are whole already, and upstream returns them as
                // they are.
                Load(request, code, 0);
                if (lane.IsFloating)
                {
                    Simd(code, (request.Intrinsic, lane.Shape) switch
                    {
                        (RuntimeIntrinsic.SimdFloor, LaneShape.F32) => F32x4Floor,
                        (RuntimeIntrinsic.SimdFloor, _) => F64x2Floor,
                        (RuntimeIntrinsic.SimdCeiling, LaneShape.F32) => F32x4Ceiling,
                        (RuntimeIntrinsic.SimdCeiling, _) => F64x2Ceiling,
                        (RuntimeIntrinsic.SimdTruncate, LaneShape.F32) => F32x4Truncate,
                        (RuntimeIntrinsic.SimdTruncate, _) => F64x2Truncate,
                        // Midpoints round to even, as Math.Round does by default.
                        (_, LaneShape.F32) => F32x4Nearest,
                        _ => F64x2Nearest,
                    });
                }
                return;
            case RuntimeIntrinsic.SimdConvert:
                EmitConversion(request, code, lane, result);
                return;
            case RuntimeIntrinsic.SimdWidenLower:
            case RuntimeIntrinsic.SimdWidenUpper:
                EmitWiden(request, code, lane, request.Intrinsic == RuntimeIntrinsic.SimdWidenUpper);
                return;
            case RuntimeIntrinsic.SimdNarrow:
                EmitNarrow(request, code, lane);
                return;
            case RuntimeIntrinsic.SimdReinterpret:
                Load(request, code, 0);
                return;
            case RuntimeIntrinsic.SimdLoad:
                ElementAddress(request, code, 0, lane);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedTriple(V128Load, 0, 0)));
                return;
            case RuntimeIntrinsic.SimdCreateElements:
                // Lanes fill in registers, so the stored vector forwards to its
                // next load whole.
                var parameters = request.Method.Signature.ParameterTypes;
                Get(code, request.Local(0, parameters[0]));
                Simd(code, Splat(lane));
                for (var index = 1; index < parameters.Length; index++)
                {
                    Get(code, request.Local(index, parameters[index]));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.SimdPrefixed,
                        WasmInstructionOperand.PrefixedPair(ReplaceLane(lane), (uint)index)));
                }
                return;
            case RuntimeIntrinsic.SimdCreateBroadcast:
                Get(code, request.Local(0, request.Method.Signature.ParameterTypes[0]));
                Simd(code, Splat(lane));
                return;
            case RuntimeIntrinsic.SimdShiftLeft:
            case RuntimeIntrinsic.SimdShiftRightArithmetic:
            case RuntimeIntrinsic.SimdShiftRightLogical:
                // Floating lanes shift their bits, as upstream's Scalar<T> does,
                // and Wasm masks the count by the lane width, as .NET does.
                Load(request, code, 0);
                Get(code, request.Local(1, CliValueKind.I4));
                Simd(code, Shift(request.Intrinsic, lane));
                return;
            case RuntimeIntrinsic.SimdConditionalSelect:
                Load(request, code, 1);
                Load(request, code, 2);
                Load(request, code, 0);
                Simd(code, V128BitSelect);
                return;
            case RuntimeIntrinsic.SimdOnesComplement:
                Load(request, code, 0);
                Simd(code, V128Not);
                return;
            case RuntimeIntrinsic.SimdNegate:
                Load(request, code, 0);
                Simd(code, lane.Shape switch
                {
                    LaneShape.I8 => I8x16Negate,
                    LaneShape.I16 => I16x8Negate,
                    LaneShape.I32 => I32x4Negate,
                    LaneShape.I64 => I64x2Negate,
                    LaneShape.F32 => F32x4Negate,
                    _ => F64x2Negate,
                });
                return;
            case RuntimeIntrinsic.SimdAbsolute:
                Load(request, code, 0);
                if (!lane.Unsigned)
                {
                    // Integer lanes wrap at MinValue, as upstream's Scalar<T>.Abs does.
                    Simd(code, lane.Shape switch
                    {
                        LaneShape.I8 => I8x16Absolute,
                        LaneShape.I16 => I16x8Absolute,
                        LaneShape.I32 => I32x4Absolute,
                        LaneShape.I64 => I64x2Absolute,
                        LaneShape.F32 => F32x4Absolute,
                        _ => F64x2Absolute,
                    });
                }
                return;
            case RuntimeIntrinsic.SimdEquals:
                Compare(request, code, lane, Comparison.Equal);
                return;
            case RuntimeIntrinsic.SimdLessThan:
                Compare(request, code, lane, Comparison.LessThan);
                return;
            case RuntimeIntrinsic.SimdGreaterThan:
                Compare(request, code, lane, Comparison.GreaterThan);
                return;
            case RuntimeIntrinsic.SimdLessThanOrEqual:
                Compare(request, code, lane, Comparison.LessOrEqual);
                return;
            case RuntimeIntrinsic.SimdGreaterThanOrEqual:
                Compare(request, code, lane, Comparison.GreaterOrEqual);
                return;
            case RuntimeIntrinsic.SimdMinimum or RuntimeIntrinsic.SimdMaximum
                when lane.Shape == LaneShape.I64:
                // No 64-bit integer minimum or maximum: select by comparison.
                Load(request, code, 0);
                Load(request, code, 1);
                Compare(
                    request,
                    code,
                    lane,
                    request.Intrinsic == RuntimeIntrinsic.SimdMaximum
                        ? Comparison.GreaterThan
                        : Comparison.LessThan);
                Simd(code, V128BitSelect);
                return;
            case RuntimeIntrinsic.SimdMultiply:
                Multiply(request, code, lane);
                return;
            case RuntimeIntrinsic.SimdMultiplyAddEstimate when lane.Shape == LaneShape.F32:
                // Each half widens to double, where the product of two floats is
                // exact, so the sum rounds once before narrowing: float's own
                // MultiplyAddEstimate, and the fused result desktop gives.
                foreach (var upper in (ReadOnlySpan<bool>)[false, true])
                {
                    for (var operand = 0; operand < 3; operand++)
                    {
                        Load(request, code, operand);
                        if (upper)
                        {
                            Load(request, code, operand);
                            Shuffle(code, UpperWords);
                        }
                        Simd(code, F64x2PromoteLowF32x4);
                        if (operand == 1) Simd(code, F64x2Multiply);
                    }
                    Simd(code, F64x2Add);
                    Simd(code, F32x4DemoteF64x2Zero);
                }
                Shuffle(code, LowWords);
                return;
            case RuntimeIntrinsic.SimdMultiplyAddEstimate:
                // Wasm SIMD has no fused multiply-add, and double has no wider
                // type, so the product rounds before the addend is added, as
                // upstream's estimate does on hardware without FMA and as Mono
                // computes it.
                Multiply(request, code, lane);
                Load(request, code, 2);
                Simd(code, Binary(RuntimeIntrinsic.SimdAdd, lane));
                return;
        }

        Load(request, code, 0);
        Load(request, code, 1);
        Simd(code, Binary(request.Intrinsic, lane));
    }

    // The product of the first two operands, either of which may be a scalar
    // that every lane multiplies by.
    private static void Multiply(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, Lane lane)
    {
        Operand(request, code, 0, lane);
        Operand(request, code, 1, lane);
        if (lane.Shape != LaneShape.I8)
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

    // Lane by lane, as upstream's Scalar<T>.Divide: a zero divisor throws, and
    // so does MinValue / -1 in 32-bit and 64-bit signed lanes, whose quotient
    // overflows. Narrower lanes divide as int and keep the low bits. The checks
    // all precede the division, which has no other effect, so the first lane
    // that fails decides the exception as it does upstream.
    private void EmitDivisionChecks(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane)
    {
        var wide = lane.Shape == LaneShape.I64;
        var overflows = !lane.Unsigned && lane.Shape is LaneShape.I32 or LaneShape.I64;
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
        Lane lane,
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
    private static void IntegerSquareRoot(IWasmInstructionWriter code, Lane lane)
    {
        var (convert, root, truncate) = (lane.Shape, lane.Unsigned) switch
        {
            (LaneShape.I8 or LaneShape.I16, false) =>
                (WasmOpcodes.F32ConvertI32Signed, WasmOpcodes.F32SquareRoot, WasmOpcodes.I32TruncateSaturateF32Signed),
            (LaneShape.I8 or LaneShape.I16, true) =>
                (WasmOpcodes.F32ConvertI32Unsigned, WasmOpcodes.F32SquareRoot, WasmOpcodes.I32TruncateSaturateF32Unsigned),
            (LaneShape.I32, false) =>
                (WasmOpcodes.F64ConvertI32Signed, WasmOpcodes.F64SquareRoot, WasmOpcodes.I32TruncateSaturateF64Signed),
            (LaneShape.I32, true) =>
                (WasmOpcodes.F64ConvertI32Unsigned, WasmOpcodes.F64SquareRoot, WasmOpcodes.I32TruncateSaturateF64Unsigned),
            (_, false) =>
                (WasmOpcodes.F64ConvertI64Signed, WasmOpcodes.F64SquareRoot, WasmOpcodes.I64TruncateSaturateF64Signed),
            _ => (WasmOpcodes.F64ConvertI64Unsigned, WasmOpcodes.F64SquareRoot, WasmOpcodes.I64TruncateSaturateF64Unsigned),
        };
        code.Write(WasmInstruction.NoOperand(convert));
        code.Write(WasmInstruction.NoOperand(root));
        Saturate(code, truncate);
    }

    // The conversions between float and int lanes have SIMD instructions; the
    // 64-bit ones convert lane by lane. Floating values truncate toward zero and
    // saturate, with NaN becoming 0, as ConvertToInt32 and the rest do.
    private static void EmitConversion(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane,
        Lane result)
    {
        switch (lane.Shape, result.Shape)
        {
            case (LaneShape.F32, LaneShape.I32):
                Load(request, code, 0);
                Simd(code, result.Unsigned ? I32x4TruncateSaturateF32x4Unsigned : I32x4TruncateSaturateF32x4Signed);
                return;
            case (LaneShape.I32, LaneShape.F32):
                Load(request, code, 0);
                Simd(code, lane.Unsigned ? F32x4ConvertI32x4Unsigned : F32x4ConvertI32x4Signed);
                return;
            case (LaneShape.I64, LaneShape.F64):
                PerLane(code, result, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    code.Write(WasmInstruction.NoOperand(
                        lane.Unsigned ? WasmOpcodes.F64ConvertI64Unsigned : WasmOpcodes.F64ConvertI64Signed));
                });
                return;
            case (LaneShape.F64, LaneShape.I64):
                PerLane(code, result, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    Saturate(code, result.Unsigned
                        ? WasmOpcodes.I64TruncateSaturateF64Unsigned
                        : WasmOpcodes.I64TruncateSaturateF64Signed);
                });
                return;
            default:
                throw Unsupported(request.Intrinsic, lane);
        }
    }

    private static void EmitWiden(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane,
        bool upper)
    {
        Load(request, code, 0);
        if (lane.Shape == LaneShape.F32)
        {
            if (upper)
            {
                Load(request, code, 0);
                Shuffle(code, UpperWords);
            }
            Simd(code, F64x2PromoteLowF32x4);
            return;
        }

        Simd(code, (lane.Shape, upper, lane.Unsigned) switch
        {
            (LaneShape.I8, false, false) => I16x8ExtendLowI8x16Signed,
            (LaneShape.I8, true, false) => I16x8ExtendHighI8x16Signed,
            (LaneShape.I8, false, true) => I16x8ExtendLowI8x16Unsigned,
            (LaneShape.I8, true, true) => I16x8ExtendHighI8x16Unsigned,
            (LaneShape.I16, false, false) => I32x4ExtendLowI16x8Signed,
            (LaneShape.I16, true, false) => I32x4ExtendHighI16x8Signed,
            (LaneShape.I16, false, true) => I32x4ExtendLowI16x8Unsigned,
            (LaneShape.I16, true, true) => I32x4ExtendHighI16x8Unsigned,
            (LaneShape.I32, false, false) => I64x2ExtendLowI32x4Signed,
            (LaneShape.I32, true, false) => I64x2ExtendHighI32x4Signed,
            (LaneShape.I32, false, true) => I64x2ExtendLowI32x4Unsigned,
            (LaneShape.I32, true, true) => I64x2ExtendHighI32x4Unsigned,
            _ => throw Unsupported(request.Intrinsic, lane),
        });
    }

    // Narrow truncates each lane, so it keeps the low half of every lane of
    // both vectors; Wasm's narrowing instructions saturate instead.
    private static void EmitNarrow(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane)
    {
        if (lane.Shape == LaneShape.F64)
        {
            Load(request, code, 0);
            Simd(code, F32x4DemoteF64x2Zero);
            Load(request, code, 1);
            Simd(code, F32x4DemoteF64x2Zero);
            Shuffle(code, LowWords);
            return;
        }

        Load(request, code, 0);
        Load(request, code, 1);
        Shuffle(code, lane.Shape switch
        {
            LaneShape.I16 => EvenBytes,
            LaneShape.I32 => EvenHalfwords,
            LaneShape.I64 => EvenWords,
            _ => throw Unsupported(request.Intrinsic, lane),
        });
    }

    // Pushes the sum of lanes [first, first + count) as the sum of its halves,
    // the order of the hardware's horizontal adds, so floating lanes sum as on
    // desktop. Integer sums wrap and do not depend on the order.
    private static void SumLanes(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane,
        int first,
        int count)
    {
        if (count == 1)
        {
            LoadLane(request, code, 0, lane, first);
            return;
        }

        SumLanes(request, code, lane, first, count / 2);
        SumLanes(request, code, lane, first + count / 2, count / 2);
        code.Write(WasmInstruction.NoOperand(lane.Shape switch
        {
            LaneShape.I64 => WasmOpcodes.I64Add,
            LaneShape.F32 => WasmOpcodes.F32Add,
            LaneShape.F64 => WasmOpcodes.F64Add,
            _ => WasmOpcodes.I32Add,
        }));
    }

    // An 8-bit or 16-bit result keeps its element type's range.
    private static void Normalize(IWasmInstructionWriter code, Lane lane)
    {
        switch (lane.Shape, lane.Unsigned)
        {
            case (LaneShape.I8, false):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend8Signed));
                break;
            case (LaneShape.I16, false):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend16Signed));
                break;
            case (LaneShape.I8 or LaneShape.I16, true):
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(lane.Shape == LaneShape.I8 ? 0xFF : 0xFFFF)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
                break;
        }
    }

    // Builds a vector of the result's lanes from the scalar each index pushes.
    private static void PerLane(IWasmInstructionWriter code, Lane result, Action<int> push)
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

    // Pushes one lane of a vector argument: extracted from a vector value, or
    // read from the storage a receiver's address points to.
    private static void LoadLane(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argument,
        Lane lane,
        int index)
    {
        var kind = request.Instruction.Stack[request.ArgumentBase + argument];
        if (kind == CliValueKind.V128)
        {
            Get(code, request.Local(argument, CliValueKind.V128));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedPair(ExtractLane(lane), (uint)index)));
            return;
        }
        Get(code, request.Local(argument, kind));
        var (opcode, alignment) = (lane.Shape, lane.Unsigned) switch
        {
            (LaneShape.I8, false) => (WasmOpcodes.I32Load8Signed, 0u),
            (LaneShape.I8, true) => (WasmOpcodes.I32Load8Unsigned, 0u),
            (LaneShape.I16, false) => (WasmOpcodes.I32Load16Signed, 1u),
            (LaneShape.I16, true) => (WasmOpcodes.I32Load16Unsigned, 1u),
            (LaneShape.I32, _) => (WasmOpcodes.I32Load, 2u),
            (LaneShape.I64, _) => (WasmOpcodes.I64Load, 3u),
            (LaneShape.F32, _) => (WasmOpcodes.F32Load, 2u),
            _ => (WasmOpcodes.F64Load, 3u),
        };
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(alignment, (uint)(index * LaneSize(lane)))));
    }

    private static void Constant(IWasmInstructionWriter code, bool wide, long value) =>
        code.Write(wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed((int)value)));

    private static void Saturate(IWasmInstructionWriter code, byte truncate) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(truncate)));

    private static void Shuffle(IWasmInstructionWriter code, byte[] lanes) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)I8x16Shuffle, .. lanes])));

    private static int Lanes(Lane lane) => 16 / LaneSize(lane);

    private static uint Binary(RuntimeIntrinsic intrinsic, Lane lane) => intrinsic switch
    {
        RuntimeIntrinsic.SimdBitwiseAnd => V128And,
        RuntimeIntrinsic.SimdBitwiseOr => V128Or,
        RuntimeIntrinsic.SimdExclusiveOr => V128Xor,
        RuntimeIntrinsic.SimdAndNot => V128AndNot,
        RuntimeIntrinsic.SimdAdd => lane.Shape switch
        {
            LaneShape.I8 => I8x16Add,
            LaneShape.I16 => I16x8Add,
            LaneShape.I32 => I32x4Add,
            LaneShape.I64 => I64x2Add,
            LaneShape.F32 => F32x4Add,
            _ => F64x2Add,
        },
        RuntimeIntrinsic.SimdSubtract => lane.Shape switch
        {
            LaneShape.I8 => I8x16Subtract,
            LaneShape.I16 => I16x8Subtract,
            LaneShape.I32 => I32x4Subtract,
            LaneShape.I64 => I64x2Subtract,
            LaneShape.F32 => F32x4Subtract,
            _ => F64x2Subtract,
        },
        RuntimeIntrinsic.SimdMultiply => lane.Shape switch
        {
            LaneShape.I16 => I16x8Multiply,
            LaneShape.I32 => I32x4Multiply,
            LaneShape.I64 => I64x2Multiply,
            LaneShape.F32 => F32x4Multiply,
            LaneShape.F64 => F64x2Multiply,
            _ => throw Unsupported(intrinsic, lane),
        },
        RuntimeIntrinsic.SimdMinimum => (lane.Shape, lane.Unsigned) switch
        {
            (LaneShape.I8, false) => I8x16MinimumSigned,
            (LaneShape.I8, true) => I8x16MinimumUnsigned,
            (LaneShape.I16, false) => I16x8MinimumSigned,
            (LaneShape.I16, true) => I16x8MinimumUnsigned,
            (LaneShape.I32, false) => I32x4MinimumSigned,
            (LaneShape.I32, true) => I32x4MinimumUnsigned,
            // IEEE 754:2019 minimum, as upstream's float.Min and double.Min.
            (LaneShape.F32, _) => F32x4Minimum,
            (LaneShape.F64, _) => F64x2Minimum,
            _ => throw Unsupported(intrinsic, lane),
        },
        RuntimeIntrinsic.SimdMaximum => (lane.Shape, lane.Unsigned) switch
        {
            (LaneShape.I8, false) => I8x16MaximumSigned,
            (LaneShape.I8, true) => I8x16MaximumUnsigned,
            (LaneShape.I16, false) => I16x8MaximumSigned,
            (LaneShape.I16, true) => I16x8MaximumUnsigned,
            (LaneShape.I32, false) => I32x4MaximumSigned,
            (LaneShape.I32, true) => I32x4MaximumUnsigned,
            (LaneShape.F32, _) => F32x4Maximum,
            (LaneShape.F64, _) => F64x2Maximum,
            _ => throw Unsupported(intrinsic, lane),
        },
        _ => throw Unsupported(intrinsic, lane),
    };

    // Pushes the comparison mask of the first two arguments. Wasm compares
    // 64-bit lanes only as signed, so unsigned ones compare with the sign bit
    // flipped.
    private static void Compare(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        Lane lane,
        Comparison comparison)
    {
        var flip = lane.Shape == LaneShape.I64 && lane.Unsigned && comparison != Comparison.Equal;
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
            (Comparison.Equal, LaneShape.I8, _) => I8x16Equal,
            (Comparison.Equal, LaneShape.I16, _) => I16x8Equal,
            (Comparison.Equal, LaneShape.I32, _) => I32x4Equal,
            (Comparison.Equal, LaneShape.I64, _) => I64x2Equal,
            (Comparison.Equal, LaneShape.F32, _) => F32x4Equal,
            (Comparison.Equal, LaneShape.F64, _) => F64x2Equal,
            (Comparison.LessThan, LaneShape.I8, false) => I8x16LessThanSigned,
            (Comparison.LessThan, LaneShape.I8, true) => I8x16LessThanUnsigned,
            (Comparison.LessThan, LaneShape.I16, false) => I16x8LessThanSigned,
            (Comparison.LessThan, LaneShape.I16, true) => I16x8LessThanUnsigned,
            (Comparison.LessThan, LaneShape.I32, false) => I32x4LessThanSigned,
            (Comparison.LessThan, LaneShape.I32, true) => I32x4LessThanUnsigned,
            (Comparison.LessThan, LaneShape.I64, _) => I64x2LessThanSigned,
            (Comparison.LessThan, LaneShape.F32, _) => F32x4LessThan,
            (Comparison.LessThan, LaneShape.F64, _) => F64x2LessThan,
            (Comparison.GreaterThan, LaneShape.I8, false) => I8x16GreaterThanSigned,
            (Comparison.GreaterThan, LaneShape.I8, true) => I8x16GreaterThanUnsigned,
            (Comparison.GreaterThan, LaneShape.I16, false) => I16x8GreaterThanSigned,
            (Comparison.GreaterThan, LaneShape.I16, true) => I16x8GreaterThanUnsigned,
            (Comparison.GreaterThan, LaneShape.I32, false) => I32x4GreaterThanSigned,
            (Comparison.GreaterThan, LaneShape.I32, true) => I32x4GreaterThanUnsigned,
            (Comparison.GreaterThan, LaneShape.I64, _) => I64x2GreaterThanSigned,
            (Comparison.GreaterThan, LaneShape.F32, _) => F32x4GreaterThan,
            (Comparison.GreaterThan, LaneShape.F64, _) => F64x2GreaterThan,
            (Comparison.LessOrEqual, LaneShape.I8, false) => I8x16LessOrEqualSigned,
            (Comparison.LessOrEqual, LaneShape.I8, true) => I8x16LessOrEqualUnsigned,
            (Comparison.LessOrEqual, LaneShape.I16, false) => I16x8LessOrEqualSigned,
            (Comparison.LessOrEqual, LaneShape.I16, true) => I16x8LessOrEqualUnsigned,
            (Comparison.LessOrEqual, LaneShape.I32, false) => I32x4LessOrEqualSigned,
            (Comparison.LessOrEqual, LaneShape.I32, true) => I32x4LessOrEqualUnsigned,
            (Comparison.LessOrEqual, LaneShape.I64, _) => I64x2LessOrEqualSigned,
            (Comparison.LessOrEqual, LaneShape.F32, _) => F32x4LessOrEqual,
            (Comparison.LessOrEqual, LaneShape.F64, _) => F64x2LessOrEqual,
            (Comparison.GreaterOrEqual, LaneShape.I8, false) => I8x16GreaterOrEqualSigned,
            (Comparison.GreaterOrEqual, LaneShape.I8, true) => I8x16GreaterOrEqualUnsigned,
            (Comparison.GreaterOrEqual, LaneShape.I16, false) => I16x8GreaterOrEqualSigned,
            (Comparison.GreaterOrEqual, LaneShape.I16, true) => I16x8GreaterOrEqualUnsigned,
            (Comparison.GreaterOrEqual, LaneShape.I32, false) => I32x4GreaterOrEqualSigned,
            (Comparison.GreaterOrEqual, LaneShape.I32, true) => I32x4GreaterOrEqualUnsigned,
            (Comparison.GreaterOrEqual, LaneShape.I64, _) => I64x2GreaterOrEqualSigned,
            (Comparison.GreaterOrEqual, LaneShape.F32, _) => F32x4GreaterOrEqual,
            _ => F64x2GreaterOrEqual,
        });
    }

    private static uint Shift(RuntimeIntrinsic intrinsic, Lane lane)
    {
        var arithmetic = intrinsic == RuntimeIntrinsic.SimdShiftRightArithmetic && !lane.Unsigned;
        return (intrinsic, lane.Bits) switch
        {
            (RuntimeIntrinsic.SimdShiftLeft, LaneShape.I8) => I8x16ShiftLeft,
            (RuntimeIntrinsic.SimdShiftLeft, LaneShape.I16) => I16x8ShiftLeft,
            (RuntimeIntrinsic.SimdShiftLeft, LaneShape.I32) => I32x4ShiftLeft,
            (RuntimeIntrinsic.SimdShiftLeft, _) => I64x2ShiftLeft,
            (_, LaneShape.I8) => arithmetic ? I8x16ShiftRightSigned : I8x16ShiftRightUnsigned,
            (_, LaneShape.I16) => arithmetic ? I16x8ShiftRightSigned : I16x8ShiftRightUnsigned,
            (_, LaneShape.I32) => arithmetic ? I32x4ShiftRightSigned : I32x4ShiftRightUnsigned,
            _ => arithmetic ? I64x2ShiftRightSigned : I64x2ShiftRightUnsigned,
        };
    }

    private static uint Splat(Lane lane) => lane.Shape switch
    {
        LaneShape.I8 => I8x16Splat,
        LaneShape.I16 => I16x8Splat,
        LaneShape.I32 => I32x4Splat,
        LaneShape.I64 => I64x2Splat,
        LaneShape.F32 => F32x4Splat,
        _ => F64x2Splat,
    };

    private static uint ReplaceLane(Lane lane) => lane.Shape switch
    {
        LaneShape.I8 => I8x16ReplaceLane,
        LaneShape.I16 => I16x8ReplaceLane,
        LaneShape.I32 => I32x4ReplaceLane,
        LaneShape.I64 => I64x2ReplaceLane,
        LaneShape.F32 => F32x4ReplaceLane,
        _ => F64x2ReplaceLane,
    };

    private static uint ExtractLane(Lane lane) => (lane.Shape, lane.Unsigned) switch
    {
        (LaneShape.I8, false) => I8x16ExtractLaneSigned,
        (LaneShape.I8, true) => I8x16ExtractLaneUnsigned,
        (LaneShape.I16, false) => I16x8ExtractLaneSigned,
        (LaneShape.I16, true) => I16x8ExtractLaneUnsigned,
        (LaneShape.I32, _) => I32x4ExtractLane,
        (LaneShape.I64, _) => I64x2ExtractLane,
        (LaneShape.F32, _) => F32x4ExtractLane,
        _ => F64x2ExtractLane,
    };

    // As upstream: an index outside the lanes throws; the element is read from
    // the vector's storage.
    private void EmitGetElement(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity element,
        Lane lane)
    {
        var size = LaneSize(lane);
        var index = request.Local(1, CliValueKind.I4);
        Get(code, index);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(16 / size)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(
            code,
            ManagedExceptionKind.ArgumentOutOfRange,
            request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        var receiver = request.Instruction.Stack[request.ArgumentBase];
        if (receiver == CliValueKind.V128)
        {
            // The element's bytes move to lane 0: each byte lane i reads byte
            // index * size + i % size.
            Get(code, request.Local(0, CliValueKind.V128));
            Get(code, index);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            Simd(code, I8x16Splat);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.FromBytes([(byte)V128Constant, .. Enumerable.Range(0, 16).Select(i => (byte)(i % size))])));
            Simd(code, I8x16Add);
            Simd(code, I8x16Swizzle);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedPair(ExtractLane(lane), 0)));
            Set(code, request.Local(0, request.Method.Signature.ReturnType));
            return;
        }

        var wide = request.Target.UsesMemory64;
        Get(code, request.Local(0, receiver));
        Get(code, index);
        if (wide)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        code.Write(wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(size))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        ManagedMemoryEmitter.EmitLoadByType(code, request.Target, 0, element, size);
        Set(code, request.Local(0, request.Method.Signature.ReturnType));
    }

    private static int LaneSize(Lane lane) => lane.Shape switch
    {
        LaneShape.I8 => 1,
        LaneShape.I16 => 2,
        LaneShape.I32 or LaneShape.F32 => 4,
        _ => 8,
    };

    // Pushes the address argument, advanced by its optional nuint element offset.
    private static void ElementAddress(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argument,
        Lane lane)
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

    private static uint AllTrue(LaneShape bits) => bits switch
    {
        LaneShape.I8 => I8x16AllTrue,
        LaneShape.I16 => I16x8AllTrue,
        LaneShape.I32 => I32x4AllTrue,
        _ => I64x2AllTrue,
    };

    private static uint BitMask(LaneShape bits) => bits switch
    {
        LaneShape.I8 => I8x16BitMask,
        LaneShape.I16 => I16x8BitMask,
        LaneShape.I32 => I32x4BitMask,
        _ => I64x2BitMask,
    };

    // The element types upstream's vectors support.
    private static bool TryGetLane(CliTypeIdentity element, WasmTargetLayout target, out Lane lane)
    {
        LaneShape native = target.UsesMemory64 ? LaneShape.I64 : LaneShape.I32;
        Lane? resolved = element.CanonicalName switch
        {
            "primitive:i1" => new(LaneShape.I8, false),
            "primitive:u1" => new(LaneShape.I8, true),
            "primitive:i2" => new(LaneShape.I16, false),
            "primitive:u2" => new(LaneShape.I16, true),
            "primitive:i4" => new(LaneShape.I32, false),
            "primitive:u4" => new(LaneShape.I32, true),
            "primitive:i8" => new(LaneShape.I64, false),
            "primitive:u8" => new(LaneShape.I64, true),
            "primitive:nativeint" => new(native, false),
            "primitive:nativeuint" => new(native, true),
            "primitive:f4" => new(LaneShape.F32, false),
            "primitive:f8" => new(LaneShape.F64, false),
            _ => null,
        };
        lane = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    private static void Load(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int argument)
    {
        // A vector value, or the address of an instance method's receiver.
        var kind = request.Instruction.Stack[request.ArgumentBase + argument];
        Get(code, request.Local(argument, kind));
        if (kind != CliValueKind.V128)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedTriple(V128Load, 0, 0)));
        }
    }

    // A vector operand, or a scalar operand broadcast to every lane.
    private static void Operand(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int argument, Lane lane)
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

    private static void Simd(IWasmInstructionWriter code, uint opcode) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.Prefixed(opcode)));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static InvalidOperationException Unsupported(RuntimeIntrinsic intrinsic, Lane lane) =>
        new($"{intrinsic} has no lowering for {lane.Shape} lanes.");
}
