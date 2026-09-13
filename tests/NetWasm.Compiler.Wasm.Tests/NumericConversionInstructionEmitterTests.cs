using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NumericConversionInstructionEmitterTests
{
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
    [InlineData(CilOperation.ConvertNativeInt, CliValueKind.F4, "native integer")]
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
                            if (native && source is CliValueKind.F4 or CliValueKind.F8)
                            {
                                continue;
                            }

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
