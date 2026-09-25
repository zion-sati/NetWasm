using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FloatingRemainderEmitterTests
{
    [Theory]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    public void EmitsExactScalingSubtractionAndSignRestoration(CliValueKind type)
    {
        var emitter = ThroughContract(new FloatingRemainderEmitter());
        var writer = new RecordingInstructionWriter();

        emitter.Emit(writer, type, 2, 3, 5, 7);

        var instructions = writer.ToInstructions();
        var opcodes = instructions.Select(instruction => instruction.Opcode).ToArray();
        var single = type == CliValueKind.F4;
        Assert.Equal(single ? 82 : 80, instructions.Length);
        Assert.Equal(2, opcodes.Count(opcode => opcode == WasmOpcodes.Loop));
        Assert.Equal([1U, 0U, 1U, 0U], instructions
            .Where(instruction => instruction.Opcode is WasmOpcodes.Branch or WasmOpcodes.BranchIf)
            .Select(instruction => instruction.Operand.UnsignedValue));
        Assert.Equal([2U, 3U, 5U, 7U], instructions
            .Where(instruction => instruction.Opcode is WasmOpcodes.LocalGet or WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue).Distinct().Order());
        Assert.Equal([3U, 7U, 2U, 5U, 3U, 5U, 2U, 3U, 5U, 2U, 2U], instructions
            .Where(instruction => instruction.Opcode == WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue));
        Assert.Equal([double.PositiveInfinity, 0, 0.5, 2, 0.5, double.NaN], instructions
            .Where(instruction => instruction.Opcode is WasmOpcodes.F32Constant or WasmOpcodes.F64Constant)
            .Select(instruction => single ? instruction.Operand.Float32Value : instruction.Operand.Float64Value));
        Assert.Contains(single ? WasmOpcodes.F32Subtract : WasmOpcodes.F64Subtract, opcodes);
        Assert.Contains(single ? WasmOpcodes.F32CopySign : WasmOpcodes.F64CopySign, opcodes);
        Assert.Contains(single ? WasmOpcodes.I32ReinterpretF32 : WasmOpcodes.I64ReinterpretF64, opcodes);
        Assert.Contains(single ? WasmOpcodes.F32ReinterpretI32 : WasmOpcodes.F64ReinterpretI64, opcodes);
        Assert.DoesNotContain(WasmOpcodes.F32Divide, opcodes);
        Assert.DoesNotContain(WasmOpcodes.F64Divide, opcodes);
        Assert.DoesNotContain(WasmOpcodes.Call, opcodes);
        Assert.DoesNotContain(WasmOpcodes.Throw, opcodes);
        Assert.Equal(WasmOpcodes.End, opcodes[^1]);
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.NativeInt)]
    public void RejectsNonFloatingOperandsBeforeWriting(CliValueKind type)
    {
        var emitter = ThroughContract(new FloatingRemainderEmitter());
        var writer = new RecordingInstructionWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(writer, type, 2, 3, 5, 7));

        Assert.Empty(writer.ToInstructions());
    }

    private static IFloatingRemainderEmitter ThroughContract(IFloatingRemainderEmitter emitter) => emitter;
}
