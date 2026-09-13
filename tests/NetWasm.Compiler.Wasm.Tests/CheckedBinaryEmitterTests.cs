using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class CheckedBinaryEmitterTests
{
    [Theory]
    [InlineData(CilOperation.AddCheckedUnsigned, WasmTarget.Wasm32, CliValueKind.I4, WasmOpcodes.I32Add, WasmOpcodes.I32LessThanUnsigned)]
    [InlineData(CilOperation.SubtractCheckedUnsigned, WasmTarget.Wasm32, CliValueKind.I4, WasmOpcodes.I32Subtract, WasmOpcodes.I32LessThanUnsigned)]
    [InlineData(CilOperation.AddCheckedUnsigned, WasmTarget.Wasm64, CliValueKind.I8, WasmOpcodes.I64Add, WasmOpcodes.I64LessThanUnsigned)]
    [InlineData(CilOperation.SubtractCheckedUnsigned, WasmTarget.Wasm64, CliValueKind.I8, WasmOpcodes.I64Subtract, WasmOpcodes.I64LessThanUnsigned)]
    public void EmitsUnsignedArithmeticAndOverflowComparison(
        CilOperation operation,
        WasmTarget target,
        CliValueKind type,
        byte arithmeticOpcode,
        byte comparisonOpcode)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        Action<ICheckedBinaryEmitter> contract = emitter =>
            emitter.Emit(writer, operation, type, 0, 1, 2);

        contract(new CheckedBinaryEmitter(
            WasmTargetLayout.For(target),
            new ImplicitExceptionEmitter(layouts, layouts, 7)));

        var bytes = writer.ToArray();
        Assert.Contains(arithmeticOpcode, bytes);
        Assert.Contains(comparisonOpcode, bytes);
        Assert.Contains(WasmOpcodes.If, bytes);
        Assert.Contains(WasmOpcodes.Throw, bytes);
    }

    [Fact]
    public void RejectsAnUncheckedOperation()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new CheckedBinaryEmitter(
            WasmTargetLayout.Wasm32,
            new ImplicitExceptionEmitter(layouts, layouts, 7));

        var error = Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(
                new EmitterTestSupport.RecordingInstructionWriter(),
                CilOperation.Add,
                CliValueKind.I4,
                0,
                1,
                2));

        Assert.Contains("not a checked binary operation", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CilOperation.AddChecked, WasmTarget.Wasm32, CliValueKind.I4,
        WasmOpcodes.I32Add, WasmOpcodes.I32Xor)]
    [InlineData(CilOperation.SubtractChecked, WasmTarget.Wasm64, CliValueKind.I8,
        WasmOpcodes.I64Subtract, WasmOpcodes.I64Xor)]
    [InlineData(CilOperation.SubtractChecked, WasmTarget.Wasm32, CliValueKind.I4,
        WasmOpcodes.I32Subtract, WasmOpcodes.I32Xor)]
    [InlineData(CilOperation.AddChecked, WasmTarget.Wasm64, CliValueKind.NativeInt,
        WasmOpcodes.I64Add, WasmOpcodes.I64LessThanSigned)]
    [InlineData(CilOperation.MultiplyChecked, WasmTarget.Wasm32, CliValueKind.I4,
        WasmOpcodes.I32Multiply, WasmOpcodes.I32DivideSigned)]
    [InlineData(CilOperation.MultiplyChecked, WasmTarget.Wasm64, CliValueKind.NativeInt,
        WasmOpcodes.I64Multiply, WasmOpcodes.I64DivideSigned)]
    [InlineData(CilOperation.MultiplyCheckedUnsigned, WasmTarget.Wasm32, CliValueKind.I4,
        WasmOpcodes.I32Multiply, WasmOpcodes.I32DivideUnsigned)]
    [InlineData(CilOperation.MultiplyCheckedUnsigned, WasmTarget.Wasm64, CliValueKind.I8,
        WasmOpcodes.I64Multiply, WasmOpcodes.I64DivideUnsigned)]
    public void EmitsEverySignedAndMultiplicationWidth(
        CilOperation operation,
        WasmTarget target,
        CliValueKind type,
        byte arithmeticOpcode,
        byte overflowOpcode)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = ThroughContract(new CheckedBinaryEmitter(
            WasmTargetLayout.For(target),
            new ImplicitExceptionEmitter(layouts, layouts, 7)));

        emitter.Emit(writer, operation, type, 0, 1, 2);

        Assert.Contains(arithmeticOpcode, writer.ToArray());
        Assert.Contains(overflowOpcode, writer.ToArray());
        Assert.Contains(WasmOpcodes.Throw, writer.ToArray());
    }

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(0, -1, 2)]
    [InlineData(0, 1, -1)]
    public void RejectsNegativeLocalIndices(
        int leftLocal,
        int rightLocal,
        int resultLocal)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = ThroughContract(new CheckedBinaryEmitter(
            WasmTargetLayout.Wasm32,
            new ImplicitExceptionEmitter(layouts, layouts, 7)));

        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(
            new EmitterTestSupport.RecordingInstructionWriter(),
            CilOperation.AddChecked,
            CliValueKind.I4,
            leftLocal,
            rightLocal,
            resultLocal));
    }

    private static ICheckedBinaryEmitter ThroughContract(
        CheckedBinaryEmitter emitter) => new[]
        {
            emitter,
        }.Cast<ICheckedBinaryEmitter>().Single();
}
