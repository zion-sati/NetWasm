using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NumericOperatorEmitterTests
{
    [Theory]
    [InlineData(CilOperation.Add, WasmOpcodes.I32Add)]
    [InlineData(CilOperation.Subtract, WasmOpcodes.I32Subtract)]
    [InlineData(CilOperation.Multiply, WasmOpcodes.I32Multiply)]
    [InlineData(CilOperation.BitwiseAnd, WasmOpcodes.I32And)]
    [InlineData(CilOperation.BitwiseOr, WasmOpcodes.I32Or)]
    [InlineData(CilOperation.BitwiseXor, WasmOpcodes.I32Xor)]
    [InlineData(CilOperation.ShiftLeft, WasmOpcodes.I32ShiftLeft)]
    [InlineData(CilOperation.ShiftRightSigned, WasmOpcodes.I32ShiftRightSigned)]
    [InlineData(CilOperation.ShiftRightUnsigned, WasmOpcodes.I32ShiftRightUnsigned)]
    public void BinaryOperationEmitsExpectedOpcodeAndConsumesRightOperand(
        CilOperation operation,
        byte expectedOpcode)
    {
        var request = CreateRequest(operation, CliValueKind.I4, CliValueKind.I4);

        CreateEmitter(new RecordingLayoutProvider()).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([CliValueKind.I4], request.Stack);
    }

    [Theory]
    [InlineData(CliValueKind.I8, WasmOpcodes.I64Add)]
    [InlineData(CliValueKind.F4, WasmOpcodes.F32Add)]
    [InlineData(CliValueKind.F8, WasmOpcodes.F64Add)]
    public void NumericBinaryOperationSupportsAllNonNativeNumericTypes(
        CliValueKind type,
        byte expectedOpcode)
    {
        var request = CreateRequest(CilOperation.Add, type, type);

        CreateEmitter(new RecordingLayoutProvider()).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([type], request.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Add)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Add)]
    public void NativeNumericBinaryOperationUsesTargetWidth(
        WasmTarget target,
        byte expectedOpcode)
    {
        var request = CreateRequest(
            CilOperation.Add,
            CliValueKind.NativeInt,
            CliValueKind.NativeInt);

        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target))).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([CliValueKind.NativeInt], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.Add, CliValueKind.ManagedAddress, CliValueKind.I4,
        WasmTarget.Wasm32, WasmOpcodes.I32Add, CliValueKind.ManagedAddress, false)]
    [InlineData(CilOperation.Add, CliValueKind.I4, CliValueKind.ManagedAddress,
        WasmTarget.Wasm64, WasmOpcodes.I64Add, CliValueKind.ManagedAddress, true)]
    [InlineData(CilOperation.Add, CliValueKind.ManagedAddress, CliValueKind.NativeInt,
        WasmTarget.Wasm64, WasmOpcodes.I64Add, CliValueKind.ManagedAddress, false)]
    [InlineData(CilOperation.Subtract, CliValueKind.ManagedAddress, CliValueKind.I4,
        WasmTarget.Wasm64, WasmOpcodes.I64Subtract, CliValueKind.ManagedAddress, true)]
    [InlineData(CilOperation.Subtract, CliValueKind.ManagedAddress,
        CliValueKind.ManagedAddress, WasmTarget.Wasm32, WasmOpcodes.I32Subtract,
        CliValueKind.NativeInt, false)]
    public void ManagedAddressArithmeticUsesTargetWidthAndPreservesCliResult(
        CilOperation operation,
        CliValueKind left,
        CliValueKind right,
        WasmTarget target,
        byte expectedOpcode,
        CliValueKind expectedResult,
        bool expectsExtension)
    {
        var request = CreateRequest(operation, left, right);

        CreateEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target))).Emit(request);

        var code = GetCodeBytes(request);
        Assert.Contains(expectedOpcode, code);
        Assert.Equal(expectsExtension, code.Contains(WasmOpcodes.I64ExtendI32Signed));
        Assert.Equal([expectedResult], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.Negate, WasmOpcodes.I64Subtract)]
    [InlineData(CilOperation.OnesComplement, WasmOpcodes.I64Xor)]
    public void Memory64NativeUnaryOperationUsesI64(
        CilOperation operation,
        byte expectedOpcode)
    {
        var request = CreateRequest(operation, CliValueKind.NativeInt);

        CreateEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.Wasm64)).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([CliValueKind.NativeInt], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.Negate, CliValueKind.I4, WasmOpcodes.I32Subtract)]
    [InlineData(CilOperation.OnesComplement, CliValueKind.I4, WasmOpcodes.I32Xor)]
    [InlineData(CilOperation.Negate, CliValueKind.I8, WasmOpcodes.I64Subtract)]
    [InlineData(CilOperation.Negate, CliValueKind.F4, WasmOpcodes.F32Negate)]
    [InlineData(CilOperation.Negate, CliValueKind.F8, WasmOpcodes.F64Negate)]
    public void UnaryOperationSupportsIntegerAndFloatingTypes(
        CilOperation operation,
        CliValueKind type,
        byte expectedOpcode)
    {
        var request = CreateRequest(operation, type);

        CreateEmitter(new RecordingLayoutProvider()).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([type], request.Stack);
    }

    [Fact]
    public void FloatingPointOnesComplementFailsDeterministically()
    {
        var request = CreateRequest(CilOperation.OnesComplement, CliValueKind.F4);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(new RecordingLayoutProvider()).Emit(request));

        Assert.Equal(
            "ones complement requires an integer operand",
            exception.Message);
    }

    [Theory]
    [InlineData(CilOperation.BitwiseAnd, WasmOpcodes.I32And)]
    [InlineData(CilOperation.BitwiseOr, WasmOpcodes.I32Or)]
    [InlineData(CilOperation.BitwiseXor, WasmOpcodes.I32Xor)]
    public void Memory32MixedNativeIntAndInt32BitwiseOperationUsesI32(
        CilOperation operation,
        byte expectedOpcode)
    {
        var request = CreateRequest(
            operation,
            CliValueKind.NativeInt,
            CliValueKind.I4);

        CreateEmitter(new RecordingLayoutProvider()).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([CliValueKind.NativeInt], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.BitwiseAnd, WasmOpcodes.I64And)]
    [InlineData(CilOperation.BitwiseOr, WasmOpcodes.I64Or)]
    [InlineData(CilOperation.BitwiseXor, WasmOpcodes.I64Xor)]
    public void Memory64MixedNativeIntAndInt32BitwiseOperationWidensAndUsesI64(
        CilOperation operation,
        byte expectedOpcode)
    {
        var request = CreateRequest(
            operation,
            CliValueKind.NativeInt,
            CliValueKind.I4);

        CreateEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.Wasm64)).Emit(request);

        Assert.Contains(WasmOpcodes.I64ExtendI32Signed, GetCodeBytes(request));
        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([CliValueKind.NativeInt], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.BitwiseAnd, WasmOpcodes.I64And)]
    [InlineData(CilOperation.BitwiseOr, WasmOpcodes.I64Or)]
    [InlineData(CilOperation.BitwiseXor, WasmOpcodes.I64Xor)]
    public void I8BitwiseOperationUsesI64(
        CilOperation operation,
        byte expectedOpcode)
    {
        var request = CreateRequest(
            operation,
            CliValueKind.I8,
            CliValueKind.I8);

        CreateEmitter(new RecordingLayoutProvider()).Emit(request);

        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Equal([CliValueKind.I8], request.Stack);
    }

    [Fact]
    public void FloatingPointBitwiseOperationFailsDeterministically()
    {
        var request = CreateRequest(
            CilOperation.BitwiseAnd,
            CliValueKind.F4,
            CliValueKind.F4);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(new RecordingLayoutProvider()).Emit(request));

        Assert.Equal(
            "bitwise operation requires an integer stack type",
            exception.Message);
    }

    [Theory]
    [InlineData(CliValueKind.I8, CliValueKind.I4, WasmTarget.Wasm32, WasmOpcodes.I64ShiftLeft, true)]
    [InlineData(CliValueKind.I8, CliValueKind.I8, WasmTarget.Wasm32, WasmOpcodes.I64ShiftLeft, false)]
    [InlineData(CliValueKind.NativeInt, CliValueKind.I4, WasmTarget.Wasm32, WasmOpcodes.I32ShiftLeft, false)]
    [InlineData(CliValueKind.NativeInt, CliValueKind.I4, WasmTarget.Wasm64, WasmOpcodes.I64ShiftLeft, true)]
    public void IntegerShiftSupportsWideValuesAndShiftTypes(
        CliValueKind valueType,
        CliValueKind shiftType,
        WasmTarget target,
        byte expectedOpcode,
        bool expectsI32Extension)
    {
        var request = CreateRequest(
            CilOperation.ShiftLeft,
            valueType,
            shiftType);

        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target))).Emit(request);

        var code = GetCodeBytes(request);
        Assert.Contains(expectedOpcode, code);
        if (expectsI32Extension)
        {
            Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, code);
        }
        else
        {
            Assert.DoesNotContain(WasmOpcodes.I64ExtendI32Unsigned, code);
        }
        Assert.Equal([valueType], request.Stack);
    }

    [Fact]
    public void IncompatibleBinaryTypesFailDeterministically()
    {
        var request = CreateRequest(
            CilOperation.Add,
            CliValueKind.I4,
            CliValueKind.F4);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(new RecordingLayoutProvider()).Emit(request));

        Assert.Equal(
            "numeric operation has incompatible stack types",
            exception.Message);
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        params CliValueKind[] stack) =>
        CreateInstructionRequest(operation, stack);

    private static NumericOperatorEmitter CreateEmitter(
        RecordingLayoutProvider layouts) => new(layouts);

}
