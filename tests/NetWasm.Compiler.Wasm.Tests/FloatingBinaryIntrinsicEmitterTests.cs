using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FloatingBinaryIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, RuntimeIntrinsic.FloatingCopySign, CliValueKind.F4, WasmOpcodes.F32CopySign)]
    [InlineData(WasmTarget.Wasm32, RuntimeIntrinsic.FloatingCopySign, CliValueKind.F8, WasmOpcodes.F64CopySign)]
    [InlineData(WasmTarget.Wasm64, RuntimeIntrinsic.FloatingCopySign, CliValueKind.F4, WasmOpcodes.F32CopySign)]
    [InlineData(WasmTarget.Wasm64, RuntimeIntrinsic.FloatingCopySign, CliValueKind.F8, WasmOpcodes.F64CopySign)]
    [InlineData(WasmTarget.Wasm32, RuntimeIntrinsic.FloatingMaximum, CliValueKind.F4, WasmOpcodes.F32Maximum)]
    [InlineData(WasmTarget.Wasm64, RuntimeIntrinsic.FloatingMaximum, CliValueKind.F8, WasmOpcodes.F64Maximum)]
    [InlineData(WasmTarget.Wasm32, RuntimeIntrinsic.FloatingMinimum, CliValueKind.F8, WasmOpcodes.F64Minimum)]
    [InlineData(WasmTarget.Wasm64, RuntimeIntrinsic.FloatingMinimum, CliValueKind.F4, WasmOpcodes.F32Minimum)]
    public void EmitsBothOperandsInOrderAndStoresResultAtArgumentBase(
        WasmTarget target, RuntimeIntrinsic intrinsic, CliValueKind kind, byte opcode)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic, [CliValueKind.I4, kind, kind]) with
        {
            Target = WasmTargetLayout.For(target),
        };
        request = request with { Call = request.Call with { ArgumentBase = 1, Consumed = 2 } };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new FloatingBinaryIntrinsicEmitter();

        ((IRuntimeIntrinsicEmitter)emitter).Emit(request, writer);

        Assert.Equal(
            new[]
            {
                WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)request.Local(0, kind))),
                WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)request.Local(1, kind))),
                WasmInstruction.NoOperand(opcode),
                WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)request.Local(0, kind))),
            },
            writer.ToInstructions());
        Assert.Equal([CliValueKind.I4, kind, kind], request.Instruction.Stack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsWrongArityBeforeWriting(int count)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.FloatingCopySign, Enumerable.Repeat(CliValueKind.F4, count));
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new FloatingBinaryIntrinsicEmitter();

        Assert.Throws<InvalidOperationException>(() => ((IRuntimeIntrinsicEmitter)emitter).Emit(request, writer));

        Assert.Empty(writer.ToInstructions());
    }

    [Theory]
    [InlineData(CliValueKind.I4, CliValueKind.F4)]
    [InlineData(CliValueKind.F4, CliValueKind.I4)]
    [InlineData(CliValueKind.F4, CliValueKind.F8)]
    [InlineData(CliValueKind.F8, CliValueKind.F4)]
    public void RejectsNonmatchingTypesBeforeWriting(CliValueKind magnitude, CliValueKind sign)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.FloatingCopySign, [magnitude, sign]);
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new FloatingBinaryIntrinsicEmitter();

        Assert.Throws<InvalidOperationException>(() => ((IRuntimeIntrinsicEmitter)emitter).Emit(request, writer));

        Assert.Empty(writer.ToInstructions());
    }
}
