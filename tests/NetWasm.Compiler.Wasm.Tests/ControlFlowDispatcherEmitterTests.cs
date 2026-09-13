using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ControlFlowDispatcherEmitterTests
{
    [Fact]
    public void EmitsDispatcherBranchesThroughItsCapability()
    {
        var condition = Block(
            3,
            CilOperation.BranchIfTrue,
            new CilOperand.BranchTarget(20));
        var body = Block(4, CilOperation.Branch, new CilOperand.BranchTarget(10));
        var terminal = Block(6, CilOperation.Return);
        var exitBody = new StructuredSequence(
            [new StructuredCode(Block(5, CilOperation.Return))]);
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(3),
            [
                new StructuredDispatcherBlock(
                    condition,
                    new StructuredBlockId(4),
                    new StructuredBlockId(5)),
                new StructuredDispatcherBlock(body, new StructuredBlockId(3), null),
                new StructuredDispatcherBlock(terminal, null, null),
            ],
            [new StructuredDispatcherExit(new StructuredBlockId(5), exitBody)]);
        var emittedBlocks = new List<int>();
        var emittedConditions = new List<int>();
        var emittedExits = new List<StructuredSequence>();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new ControlFlowDispatcherEmitter();

        EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher), dispatcher, 7),
            block => emittedBlocks.Add(block.Occurrence.Block.Value),
            block =>
            {
                emittedConditions.Add(block.Occurrence.Block.Value);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(1)));
            },
            sequence => emittedExits.Add(sequence));

        Assert.Equal([4, 6], emittedBlocks);
        Assert.Equal([3], emittedConditions);
        Assert.Equal([exitBody], emittedExits);
        Assert.Contains(WasmOpcodes.Loop, code.ToArray());
        Assert.Contains(WasmOpcodes.Branch, code.ToArray());
        Assert.Contains(WasmOpcodes.Unreachable, code.ToArray());
    }

    [Fact]
    public void EmitsReentrantExitBodyAndResumesOwnedProgramCounter()
    {
        var emptyBody = StructuredSequence.Empty;
        var populatedBody = new StructuredSequence(
            [new StructuredCode(Block(5, CilOperation.Return))]);
        var dispatcher = new StructuredDispatcher(
            null,
            [new StructuredDispatcherBlock(
                Block(3, CilOperation.Return),
                null,
                null)],
            [
                new StructuredDispatcherExit(new StructuredBlockId(1), emptyBody),
                new StructuredDispatcherExit(new StructuredBlockId(2), populatedBody),
            ]);
        var emittedExits = new List<StructuredSequence>();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new ControlFlowDispatcherEmitter();

        EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher), dispatcher, 0),
            _ => { },
            _ => { },
            sequence => emittedExits.Add(sequence));

        Assert.Equal([populatedBody], emittedExits);
        var instructions = code.ToInstructions();
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Branch &&
            instruction.Operand == WasmInstructionOperand.Unsigned(2));
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.BranchIf &&
            instruction.Operand == WasmInstructionOperand.Unsigned(1));
    }

    [Fact]
    public void EndFinallyTerminalLeavesTheDispatcher()
    {
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(3),
            [new StructuredDispatcherBlock(
                Block(3, CilOperation.EndFinally),
                null,
                null)],
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new ControlFlowDispatcherEmitter();

        EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher, CilOperation.EndFinally), dispatcher, 7),
            _ => { },
            _ => { },
            _ => { });

        var instructions = code.ToInstructions();
        Assert.Single(instructions.Where(instruction =>
            instruction.Opcode == WasmOpcodes.Unreachable));
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Branch &&
            instruction.Operand == WasmInstructionOperand.Unsigned(2));
    }

    [Fact]
    public void RejectsMissingRequestValues()
    {
        var emitter = new ControlFlowDispatcherEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var dispatcher = new StructuredDispatcher(null, [], []);
        Action<StructuredDispatcherBlock> emitBlock = _ => { };
        Action<StructuredDispatcherBlock> emitCondition = _ => { };
        Action<StructuredSequence> emitSequence = _ => { };

        Assert.Throws<ArgumentNullException>(() => EmitThroughCapability(
            emitter,
            code,
            null!,
            emitBlock,
            emitCondition,
            emitSequence));
        Assert.Throws<ArgumentNullException>(() => EmitThroughCapability(
            emitter,
            null!,
            new(Method(dispatcher), dispatcher, 0),
            emitBlock,
            emitCondition,
            emitSequence));
        Assert.Throws<ArgumentNullException>(() => EmitThroughCapability(
            emitter,
            code,
            new(null!, dispatcher, 0),
            emitBlock,
            emitCondition,
            emitSequence));
        Assert.Throws<ArgumentNullException>(() => EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher), dispatcher, 0),
            null!,
            emitCondition,
            emitSequence));
        Assert.Throws<ArgumentNullException>(() => EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher), dispatcher, 0),
            emitBlock,
            null!,
            emitSequence));
        Assert.Throws<ArgumentNullException>(() => EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher), dispatcher, 0),
            emitBlock,
            emitCondition,
            null!));
    }

    [Fact]
    public void RejectsNegativeProgramCounterLocalAtEachEmissionBoundary()
    {
        var emitter = new ControlFlowDispatcherEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        Action<StructuredDispatcherBlock> emitBlock = _ => { };
        Action<StructuredDispatcherBlock> emitCondition = _ => { };
        Action<StructuredSequence> emitSequence = _ => { };

        Assert.Throws<ArgumentOutOfRangeException>(() => EmitThroughCapability(
            emitter,
            code,
            new(Method(new StructuredDispatcher(
                    new StructuredBlockId(0),
                    [],
                    [])),
                new StructuredDispatcher(new StructuredBlockId(0), [], []),
                -1),
            emitBlock,
            emitCondition,
            emitSequence));
        Assert.Throws<ArgumentOutOfRangeException>(() => EmitThroughCapability(
            emitter,
            code,
            new(
                Method(new StructuredDispatcher(
                    null,
                    [new StructuredDispatcherBlock(Block(0, CilOperation.Return), null, null)],
                    [])),
                new StructuredDispatcher(
                    null,
                    [new StructuredDispatcherBlock(Block(0, CilOperation.Return), null, null)],
                    []),
                -1),
            emitBlock,
            emitCondition,
            emitSequence));
    }

    private static void EmitThroughCapability(
        ControlFlowDispatcherEmitter emitter,
        IWasmInstructionWriter? code,
        ControlFlowDispatcherEmissionRequest? request,
        Action<StructuredDispatcherBlock>? emitBlock,
        Action<StructuredDispatcherBlock>? emitCondition,
        Action<StructuredSequence>? emitSequence) =>
        ((IControlFlowDispatcherEmitter)emitter).Emit(
            code!,
            request!,
            emitBlock!,
            emitCondition!,
            emitSequence!);

    private static StructuredBlockOccurrence Block(
        int index,
        CilOperation operation,
        CilOperand? operand = null) => new(
        new StructuredBlockId(index),
        StructuredBlockRole.Owner);

    private static StructuredMethod Method(
        StructuredDispatcher dispatcher,
        CilOperation terminal = CilOperation.Return)
    {
        var program = new FakeProgram();
        var definitions = dispatcher.Blocks
            .Select(block => block.Occurrence.Block)
            .Distinct()
            .ToImmutableDictionary(
                id => id,
                id =>
                {
                    var instruction = new CilInstruction(
                        id.Value * 10,
                        id.Value * 10 + 1,
                        terminal,
                        new CilOperand.None());
                    return new StructuredBlockDefinition(
                        id,
                        instruction.Offset,
                        [],
                        [],
                        new StructuredTerminalExit(instruction))
                    {
                        EndOffset = instruction.NextOffset,
                    };
                });
        return new StructuredMethod(
            new StructuredMethodHeader(
                program.GetMethod(EmitterTestSupport.EntryKey),
                null,
                1,
                [],
                [],
                [.. dispatcher.Blocks
                    .Select(block => block.Occurrence.Block)
                    .Distinct()
                    .Select(id => new CilInstruction(
                        id.Value * 10,
                        id.Value * 10 + 1,
                        terminal,
                        new CilOperand.None()))]),
            dispatcher.EntryBlock ?? definitions.Keys.FirstOrDefault(),
            definitions,
            StructuredSequence.Empty,
            [],
            ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
    }
}
