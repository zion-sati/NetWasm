using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class NumericConversionInstructionEmitter(
    ITargetLayout layouts,
    INativeIntegerConversionEmitter nativeIntegerConversions,
    IImplicitExceptionEmitter exceptions) : InstructionCommandProvider
{
    private readonly INativeIntegerConversionEmitter _nativeIntegerConversions =
        nativeIntegerConversions ??
        throw new ArgumentNullException(nameof(nativeIntegerConversions));

    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.ConvertInt32, (request, code) =>
            EmitConversion(request, code, CliValueKind.I4, false)),
        Command(CilOperation.ConvertInt32Unsigned, (request, code) =>
            EmitConversion(request, code, CliValueKind.I4, true)),
        Command(CilOperation.ConvertInt64, (request, code) =>
            EmitConversion(request, code, CliValueKind.I8, false)),
        Command(CilOperation.ConvertInt64Unsigned, (request, code) =>
            EmitConversion(request, code, CliValueKind.I8, true)),
        Command(CilOperation.ConvertNativeInt, (request, code) =>
            EmitConversion(request, code, CliValueKind.NativeInt, false)),
        Command(CilOperation.ConvertNativeUInt, (request, code) =>
            EmitConversion(request, code, CliValueKind.NativeInt, true)),
        Command(CilOperation.ConvertFloat32, (request, code) =>
            EmitConversion(request, code, CliValueKind.F4, false)),
        Command(CilOperation.ConvertFloat64, (request, code) =>
            EmitConversion(request, code, CliValueKind.F8, false)),
        Command(CilOperation.ConvertFloatUnsigned, (request, code) =>
            EmitConversion(request, code, CliValueKind.F8, true)),
        Command(CilOperation.CheckFinite, EmitCheckFinite),
        Command(CilOperation.ConvertNumeric, EmitExplicitConversion),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) =>
        new(operation, InstructionFamily.Numeric, emit);

    private void EmitConversion(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        CliValueKind destination,
        bool unsigned)
    {
        var slot = request.Stack.Count - 1;
        var source = request.Stack[slot];
        if (source == destination)
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, source)))));
        if (destination == CliValueKind.NativeInt)
        {
            _nativeIntegerConversions.Emit(code, source, unsigned);
        }
        else if (source == CliValueKind.NativeInt &&
                 destination is CliValueKind.I4 or CliValueKind.I8)
        {
            EmitFromNativeInt(code, destination, unsigned);
        }
        else
        {
            var scalarSource = source == CliValueKind.NativeInt
                ? layouts.Target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4
                : source;
            EmitScalarConversion(code, scalarSource, destination, unsigned);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, destination)))));
        request.Stack[slot] = destination;
    }

    private void EmitCheckFinite(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var slot = request.Stack.Count - 1;
        var source = request.Stack[slot];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, source)))));
        if (source == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Absolute));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32(float.MaxValue)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThanOrEqual));
        }
        else if (source == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Absolute));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Constant, WasmInstructionOperand.Float64(double.MaxValue)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThanOrEqual));
        }
        else
        {
            throw new InvalidOperationException("ckfinite requires a floating value");
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Arithmetic);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFromNativeInt(
        IWasmInstructionWriter code,
        CliValueKind destination,
        bool unsigned)
    {
        if (layouts.Target.UsesMemory64)
        {
            if (destination == CliValueKind.I4)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            }
        }
        else if (destination == CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(unsigned
                ? WasmOpcodes.I64ExtendI32Unsigned
                : WasmOpcodes.I64ExtendI32Signed));
        }
    }

    private static void EmitScalarConversion(
        IWasmInstructionWriter code,
        CliValueKind source,
        CliValueKind destination,
        bool unsigned)
    {
        switch (source, destination)
        {
            case (CliValueKind.I4, CliValueKind.I8):
                if (unsigned) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
                break;
            case (CliValueKind.I8, CliValueKind.I4):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
                break;
            case (CliValueKind.I4, CliValueKind.F4):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32ConvertI32Signed));
                break;
            case (CliValueKind.I4, CliValueKind.F8):
                if (unsigned) code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64ConvertI32Unsigned));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64ConvertI32Signed));
                break;
            case (CliValueKind.I8, CliValueKind.F4):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32ConvertI64Signed));
                break;
            case (CliValueKind.I8, CliValueKind.F8):
                if (unsigned) code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64ConvertI64Unsigned));
                else code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64ConvertI64Signed));
                break;
            case (CliValueKind.F4, CliValueKind.I4):
                if (unsigned) code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I32TruncateSaturateF32Unsigned)));
                else code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I32TruncateSaturateF32Signed)));
                break;
            case (CliValueKind.F8, CliValueKind.I4):
                if (unsigned) code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I32TruncateSaturateF64Unsigned)));
                else code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I32TruncateSaturateF64Signed)));
                break;
            case (CliValueKind.F4, CliValueKind.I8):
                if (unsigned) code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I64TruncateSaturateF32Unsigned)));
                else code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I64TruncateSaturateF32Signed)));
                break;
            case (CliValueKind.F8, CliValueKind.I8):
                if (unsigned) code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I64TruncateSaturateF64Unsigned)));
                else code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.Prefixed(WasmOpcodes.I64TruncateSaturateF64Signed)));
                break;
            case (CliValueKind.F4, CliValueKind.F8):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64PromoteF32));
                break;
            case (CliValueKind.F8, CliValueKind.F4):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32DemoteF64));
                break;
            default:
                throw new InvalidOperationException("unsupported numeric conversion");
        }
    }

    private void EmitExplicitConversion(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var conversion = request.Instruction.Operand as CilOperand.NumericConversion
            ?? throw new InvalidOperationException(
                "numeric conversion metadata is missing");
        var bits = conversion.Native
            ? layouts.Target.AddressSize * 8
            : conversion.BitWidth;
        var slot = request.Stack.Count - 1;
        var source = request.Stack[slot];
        if (conversion.Checked)
        {
            EmitBoundsCheck(
                request, code, slot,
                source,
                bits,
                conversion.DestinationUnsigned,
                conversion.SourceUnsigned);
        }
        var destination = conversion.Native
            ? CliValueKind.NativeInt
            : bits <= 32 ? CliValueKind.I4 : CliValueKind.I8;
        var unsignedConversion = source is CliValueKind.F4 or CliValueKind.F8
            ? conversion.DestinationUnsigned
            : conversion.SourceUnsigned;
        EmitConversion(request, code, destination, unsignedConversion);
        EmitNarrowing(request, code, slot, bits, conversion.DestinationUnsigned);
    }

    private void EmitNarrowing(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int slot,
        int bits,
        bool unsigned)
    {
        if (bits is not (8 or 16))
        {
            return;
        }
        var local = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals,
            slot,
            CliValueKind.I4,
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        if (unsigned)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(bits == 8 ? byte.MaxValue : ushort.MaxValue)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        }
        else if (bits == 8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend8Signed));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend16Signed));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(local))));
    }

    private void EmitBoundsCheck(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int slot,
        CliValueKind source,
        int destinationBits,
        bool destinationUnsigned,
        bool sourceUnsigned)
    {
        if (source is CliValueKind.F4 or CliValueKind.F8)
        {
            EmitFloatingBoundsCheck(
                request, code, slot,
                source,
                destinationBits,
                destinationUnsigned);
            return;
        }
        var sourceUsesI64 = source == CliValueKind.I8 ||
                            source == CliValueKind.NativeInt &&
                            layouts.Target.UsesMemory64;
        var sourceBits = sourceUsesI64 ? 64 : 32;
        if (!sourceUnsigned && destinationUnsigned)
        {
            EmitIntegerBoundCheck(request, code, slot, source, 0, true, false);
        }
        if (!destinationUnsigned)
        {
            if (!sourceUnsigned && destinationBits < sourceBits)
            {
                EmitIntegerBoundCheck(
                    request, code, slot,
                    source,
                    -(1L << (destinationBits - 1)),
                    true,
                    false);
            }
            if (destinationBits < sourceBits ||
                sourceUnsigned && destinationBits <= sourceBits)
            {
                var maximum = destinationBits == 64
                    ? long.MaxValue
                    : (1L << (destinationBits - 1)) - 1;
                EmitIntegerBoundCheck(
                    request, code, slot,
                    source,
                    maximum,
                    false,
                    sourceUnsigned);
            }
        }
        else if (destinationBits < sourceBits)
        {
            var maximum = destinationBits == 32
                ? uint.MaxValue
                : (1L << destinationBits) - 1;
            EmitIntegerBoundCheck(
                request, code, slot,
                source,
                maximum,
                false,
                sourceUnsigned);
        }
    }

    private void EmitIntegerBoundCheck(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int slot,
        CliValueKind source,
        long bound,
        bool lower,
        bool unsigned)
    {
        var usesI64 = source == CliValueKind.I8 ||
                      source == CliValueKind.NativeInt && layouts.Target.UsesMemory64;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, source)))));
        if (usesI64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(bound)));
            if (lower) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned));
            else if (unsigned) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanSigned));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(unchecked((int)bound))));
            if (lower) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
            else if (unsigned) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
        }
        EmitOverflowIfTrue(code);
    }

    private void EmitFloatingBoundsCheck(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int slot,
        CliValueKind source,
        int destinationBits,
        bool destinationUnsigned)
    {
        var local = GetStackLocal(request.Context, slot, source);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        if (source == CliValueKind.F4) code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Equal));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        EmitOverflowIfTrue(code);
        var lower = destinationUnsigned ? 0d : -Math.Pow(2d, destinationBits - 1);
        var upper = Math.Pow(
            2d,
            destinationUnsigned ? destinationBits : destinationBits - 1);
        EmitFloatingBoundCheck(code, local, source, lower, true);
        EmitFloatingBoundCheck(code, local, source, upper, false);
    }

    private void EmitFloatingBoundCheck(
        IWasmInstructionWriter code,
        int local,
        CliValueKind source,
        double bound,
        bool lower)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        // Checked CIL conversion tests the integer obtained by truncating toward
        // zero. Keep the original floating local intact for the conversion.
        code.Write(WasmInstruction.NoOperand(source == CliValueKind.F4
            ? WasmOpcodes.F32Truncate
            : WasmOpcodes.F64Truncate));
        if (source == CliValueKind.F4) code.Write(WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32((float)bound)));
        else code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Constant, WasmInstructionOperand.Float64(bound)));
        if (source == CliValueKind.F4)
        {
            if (lower) code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32LessThan));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32GreaterThanOrEqual));
        }
        else if (lower) code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64LessThan));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64GreaterThanOrEqual));
        EmitOverflowIfTrue(code);
    }

    private void EmitOverflowIfTrue(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
