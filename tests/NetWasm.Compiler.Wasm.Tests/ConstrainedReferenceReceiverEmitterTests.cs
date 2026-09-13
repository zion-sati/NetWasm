using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ConstrainedReferenceReceiverEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Load)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Load)]
    public void EmitLoadsReferenceFromManagedAddressForEveryMemoryWidth(
        WasmTarget target,
        byte expectedLoad)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var context = CreateMethodEmissionContext();
        var instruction = CreateInstructionRequest(
            CilOperation.CallVirtual,
            [CliValueKind.ManagedAddress],
            context: context);
        var code = (RecordingInstructionWriter)GetCodeWriter(instruction);
        var emitter = Assert.IsAssignableFrom<IConstrainedReferenceReceiverEmitter>(
            new ConstrainedReferenceReceiverEmitter(layouts));

        var receiver = emitter.Emit(
            instruction,
            code,
            0,
            CliTypeIdentity.Named(Assembly, "Test", "Contract", isValueType: false));

        var expectedReceiver = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.ManagedReference,
            layouts.Target);
        Assert.Equal(expectedReceiver, receiver);
        Assert.Equal(CliValueKind.ManagedReference, instruction.Stack[0]);
        var emitted = code.ToInstructions();
        Assert.Equal(WasmOpcodes.LocalGet, emitted[0].Opcode);
        Assert.Equal(expectedLoad, emitted[1].Opcode);
        Assert.Equal(WasmOpcodes.LocalSet, emitted[2].Opcode);
        Assert.Equal((uint)context.ObjectTemporary, emitted[2].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.LocalGet, emitted[3].Opcode);
        Assert.Equal(WasmOpcodes.LocalSet, emitted[4].Opcode);
        Assert.Equal((uint)expectedReceiver, emitted[4].Operand.UnsignedValue);
    }

    [Fact]
    public void EmitLeavesNonReferenceConstraintsUntouched()
    {
        var layouts = new RecordingLayoutProvider();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ManagedAddress]);
        var code = (RecordingInstructionWriter)GetCodeWriter(instruction);
        var emitter = Assert.IsAssignableFrom<IConstrainedReferenceReceiverEmitter>(
            new ConstrainedReferenceReceiverEmitter(layouts));

        var receiver = emitter.Emit(
            instruction,
            code,
            0,
            CliTypeIdentity.Named(Assembly, "Test", "Value", isValueType: true));

        Assert.Null(receiver);
        Assert.Empty(code.ToInstructions());
    }
}
