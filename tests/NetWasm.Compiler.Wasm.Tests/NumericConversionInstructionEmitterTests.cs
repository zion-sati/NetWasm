using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NumericConversionInstructionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64, WasmOpcodes.I64ExtendI32Signed)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64Unsigned, WasmOpcodes.I64ExtendI32Unsigned)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertInt64, null)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertInt64Unsigned, null)]
    public void NativeWideningPreservesSignednessAndTargetWidth(
        WasmTarget target,
        CilOperation operation,
        byte? extension)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var request = CreateRequest(operation, new CilOperand.None(), CliValueKind.NativeInt);
        var command = CreateEmitter(layouts).Commands.Single(candidate =>
            candidate.Operation == operation);
        var writer = new RecordingInstructionWriter();

        ((IInstructionCommand)command).Emit(request, writer, CreateFunctionIndexResolver());

        Assert.Equal([CliValueKind.I8], request.Stack);
        Assert.Equal(
            extension is { } opcode
                ? [WasmOpcodes.LocalGet, opcode, WasmOpcodes.LocalSet]
                : new[] { WasmOpcodes.LocalGet, WasmOpcodes.LocalSet },
            writer.ToInstructions().Select(instruction => instruction.Opcode));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F8)]
    public void CheckedFloatingBoundsCompareTruncatedValuesWithoutChangingTheSource(
        WasmTarget target, CliValueKind source)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        foreach (var width in new[] { 8, 16, 32, 64 })
        foreach (var unsigned in new[] { false, true })
        {
            var request = CreateRequest(CilOperation.ConvertNumeric,
                new CilOperand.NumericConversion(width, unsigned, true, false, false), source);
            CreateEmitter(layouts).Emit(request);
            var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions().ToList();
            var truncate = source == CliValueKind.F4 ? WasmOpcodes.F32Truncate : WasmOpcodes.F64Truncate;
            var constant = source == CliValueKind.F4 ? WasmOpcodes.F32Constant : WasmOpcodes.F64Constant;
            var comparisons = source == CliValueKind.F4
                ? new[] { WasmOpcodes.F32LessThan, WasmOpcodes.F32GreaterThanOrEqual }
                : new[] { WasmOpcodes.F64LessThan, WasmOpcodes.F64GreaterThanOrEqual };
            foreach (var comparison in comparisons)
            {
                var index = instructions.FindIndex(item => item.Opcode == comparison);
                Assert.True(index >= 3);
                Assert.Equal(WasmOpcodes.LocalGet, instructions[index - 3].Opcode);
                Assert.Equal(truncate, instructions[index - 2].Opcode);
                Assert.Equal(constant, instructions[index - 1].Opcode);
            }
            Assert.Equal(2, instructions.Count(item => item.Opcode == truncate));
            Assert.DoesNotContain(instructions.Take(instructions.FindIndex(value => value.Opcode == comparisons[1])),
                item => item.Opcode == WasmOpcodes.LocalSet);
            Assert.Equal(width <= 32 ? CliValueKind.I4 : CliValueKind.I8, Assert.Single(request.Stack));
        }
    }

    [Fact]
    public void Int32ToInt64UsesSignedExtensionAndUpdatesStack()
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateRequest(
            CilOperation.ConvertInt64,
            new CilOperand.None(),
            CliValueKind.I4);

        CreateEmitter(layouts).Emit(request);

        Assert.Equal([CliValueKind.I8], request.Stack);
        Assert.Contains(WasmOpcodes.I64ExtendI32Signed, GetCodeBytes(request));
    }

    [Fact]
    public void CheckedNarrowingEmitsManagedOverflowPath()
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateRequest(
            CilOperation.ConvertNumeric,
            new CilOperand.NumericConversion(8, false, true, false, false),
            CliValueKind.I8);

        CreateEmitter(layouts).Emit(request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Extend8Signed, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CilOperation.ConvertInt32, CliValueKind.F4)]
    [InlineData(CilOperation.ConvertInt32, CliValueKind.F8)]
    [InlineData(CilOperation.ConvertInt64, CliValueKind.F4)]
    [InlineData(CilOperation.ConvertInt64, CliValueKind.F8)]
    public void UncheckedFloatingToIntegerUsesNonTrappingSaturatingConversion(
        CilOperation operation,
        CliValueKind source)
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateRequest(operation, new CilOperand.None(), source);

        CreateEmitter(layouts).Emit(request);

        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(request));
    }

    [Fact]
    public void UnsignedIntegerToFloatUsesUnsignedConversion()
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateRequest(
            CilOperation.ConvertFloatUnsigned,
            new CilOperand.None(),
            CliValueKind.I8);

        CreateEmitter(layouts).Emit(request);

        Assert.Equal([CliValueKind.F8], request.Stack);
        Assert.Contains(WasmOpcodes.F64ConvertI64Unsigned, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    public void FiniteCheckPreservesValueAndEmitsArithmeticThrow(CliValueKind source)
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateRequest(
            CilOperation.CheckFinite,
            new CilOperand.None(),
            source);

        CreateEmitter(layouts).Emit(request);

        Assert.Equal([source], request.Stack);
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt32, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertInt32, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt32Unsigned, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt32Unsigned, CliValueKind.F8)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertInt64, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64Unsigned, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64Unsigned, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertInt64Unsigned, CliValueKind.F8)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertNativeInt, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertNativeInt, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertNativeUInt, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertNativeInt, CliValueKind.ManagedAddress)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat32, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat32, CliValueKind.F8)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat64, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat64, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat64, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloat64, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm64, CilOperation.ConvertFloat64, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm32, CilOperation.ConvertFloatUnsigned, CliValueKind.I4)]
    public void EmitsSupportedConversionVariants(
        WasmTarget target,
        CilOperation operation,
        CliValueKind source)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var request = CreateRequest(operation, new CilOperand.None(), source);

        CreateEmitter(layouts).Emit(request);

        Assert.NotEmpty(GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CilOperation.CheckFinite, CliValueKind.I4, "ckfinite")]
    [InlineData(CilOperation.ConvertNativeInt, CliValueKind.ValueType, "native integer")]
    [InlineData(CilOperation.ConvertFloat32, CliValueKind.ManagedReference, "numeric conversion")]
    public void RejectsInvalidConversionInputs(
        CilOperation operation,
        CliValueKind source,
        string message)
    {
        var request = CreateRequest(operation, new CilOperand.None(), source);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(new RecordingLayoutProvider()).Emit(request));

        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void RejectsMissingExplicitConversionMetadata()
    {
        var request = CreateRequest(
            CilOperation.ConvertNumeric,
            new CilOperand.None(),
            CliValueKind.I4);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(new RecordingLayoutProvider()).Emit(request));

        Assert.Contains("metadata is missing", exception.Message);
    }

    [Fact]
    public void ConstructorRejectsMissingNativeIntegerConversionStrategy()
    {
        var layouts = new RecordingLayoutProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new NumericConversionInstructionEmitter(
                layouts,
                null!,
                new ImplicitExceptionEmitter(layouts, layouts, 7)));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitsEveryCheckedNumericBoundaryVariant(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var sources = new[]
        {
            CliValueKind.I4,
            CliValueKind.I8,
            CliValueKind.NativeInt,
            CliValueKind.F4,
            CliValueKind.F8,
        };
        var bitWidths = new[] { 8, 16, 32, 64 };

        foreach (var source in sources)
            foreach (var bitWidth in bitWidths)
                foreach (var destinationUnsigned in new[] { false, true })
                    foreach (var sourceUnsigned in new[] { false, true })
                        foreach (var native in new[] { false, true })
                        {
                            var request = CreateRequest(
                                CilOperation.ConvertNumeric,
                                new CilOperand.NumericConversion(
                                    bitWidth,
                                    destinationUnsigned,
                                    true,
                                    sourceUnsigned,
                                    native),
                                source);

                            CreateEmitter(layouts).Emit(request);

                            Assert.Equal(
                                native
                                    ? CliValueKind.NativeInt
                                    : bitWidth <= 32 ? CliValueKind.I4 : CliValueKind.I8,
                                Assert.Single(request.Stack));
                        }
    }

    private static NumericConversionInstructionEmitter CreateEmitter(
        RecordingLayoutProvider layouts) => new(
        layouts,
        new NativeIntegerConversionEmitter(layouts),
        new ImplicitExceptionEmitter(layouts, layouts, 7));

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CilOperand operand,
        CliValueKind type)
    {
        return CreateInstructionRequest(
            operation,
            [type],
            operand);
    }
}
