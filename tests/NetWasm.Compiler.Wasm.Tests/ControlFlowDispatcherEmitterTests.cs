// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
    public void DenseDispatcherUsesConstantTimeBranchTableSelection()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        var branchTable = Assert.Single(code.ToInstructions().Where(instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable));
        Assert.Equal(32, branchTable.Operand.BranchTableTargets.Length);
        Assert.Equal(0U, branchTable.Operand.BranchTableTargets[0]);
        Assert.Equal(31U, branchTable.Operand.BranchTableTargets[31]);
        Assert.Equal(32U, branchTable.Operand.BranchTableDefault);
        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
    }

    [Fact]
    public void DenseDispatcherEmitsPopulatedExitBodiesAtTheirLoopDepths()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var firstBody = new StructuredSequence(
            [new StructuredCode(Block(40, CilOperation.Return))]);
        var secondBody = new StructuredSequence(
            [new StructuredCode(Block(41, CilOperation.Return))]);
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            [
                new StructuredDispatcherExit(new StructuredBlockId(32), firstBody),
                new StructuredDispatcherExit(new StructuredBlockId(33), secondBody),
            ]);
        var emitted = new List<(StructuredSequence Body, int LoopDepth)>();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (body, loopDepth) => emitted.Add((body, loopDepth)));

        Assert.Equal([(secondBody, 2), (firstBody, 1)], emitted);
        Assert.Single(code.ToInstructions().Where(instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable));
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchIf &&
            instruction.Operand == WasmInstructionOperand.Unsigned(2));
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchIf &&
            instruction.Operand == WasmInstructionOperand.Unsigned(1));
    }

    [Fact]
    public void DenseDispatcherEmitsConditionalCasesThroughTheBranchTable()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => index == 0
                ? new StructuredDispatcherBlock(
                    Block(index, CilOperation.BranchIfTrue),
                    new StructuredBlockId(1),
                    new StructuredBlockId(2))
                : new StructuredDispatcherBlock(
                    Block(index, CilOperation.Return),
                    null,
                    null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var conditions = new List<int>();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            block =>
            {
                conditions.Add(block.Occurrence.Block.Value);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(1)));
            },
            (_, _) => { });

        Assert.Equal([0], conditions);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.If);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.Else);
    }

    [Fact]
    public void DenseDispatcherThreadsForwardSuccessorDirectly()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                index == 0 ? new StructuredBlockId(1) : null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand == WasmInstructionOperand.Signed(1));
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.Branch &&
            instruction.Operand == WasmInstructionOperand.Unsigned(0));
    }

    [Fact]
    public void DenseDispatcherRedispatchesBackedgeSuccessor()
    {
        // A real cycle prevents topological ordering from eliminating the backedge.
        var blocks = Enumerable.Range(0, 32)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                new StructuredBlockId((index + 1) % 32),
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(31),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.Single(code.ToInstructions().Where(instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand == WasmInstructionOperand.Signed(0)));
        Assert.Equal(2, code.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand == WasmInstructionOperand.Unsigned(7)));
    }

    [Fact]
    public void DenseEndFinallyCasesLeaveTheDispatcher()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.EndFinally),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher, CilOperation.EndFinally), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.Branch &&
            instruction.Operand == WasmInstructionOperand.Unsigned(33));
    }

    [Fact]
    public void SmallDenseDispatcherKeepsCompactLinearSelection()
    {
        var blocks = Enumerable.Range(0, 31)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
    }

    [Fact]
    public void SparseDispatcherKeepsCompactLinearSelection()
    {
        var blocks = ImmutableArray.Create(0, 100, 101, 102)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
    }

    [Fact]
    public void LargeSparseDispatcherKeepsLinearSelection()
    {
        var blocks = Enumerable.Range(0, 31)
            .Append(128)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
    }

    [Fact]
    public void NegativeDispatcherTargetKeepsLinearSelection()
    {
        var blocks = Enumerable.Range(0, 31)
            .Prepend(-1)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            []);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
    }

    [Fact]
    public void DuplicateDispatcherTargetKeepsLinearSelection()
    {
        var blocks = Enumerable.Range(0, 32)
            .Select(index => new StructuredDispatcherBlock(
                Block(index, CilOperation.Return),
                null,
                null))
            .ToImmutableArray();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(0),
            blocks,
            [new StructuredDispatcherExit(
                new StructuredBlockId(0),
                StructuredSequence.Empty)]);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        EmitThroughCapability(
            CreateEmitter(),
            code,
            new(Method(dispatcher), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
    }

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
        var emittedExits = new List<(StructuredSequence Body, int LoopDepth)>();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = CreateEmitter();

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
            (sequence, loopDepth) => emittedExits.Add((sequence, loopDepth)));

        Assert.Equal([4, 6], emittedBlocks);
        Assert.Equal([3], emittedConditions);
        Assert.Equal([(exitBody, 1)], emittedExits);
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
            [
                new StructuredDispatcherBlock(
                    Block(3, CilOperation.Return),
                    null,
                    null),
                new StructuredDispatcherBlock(
                    Block(4, CilOperation.Return),
                    null,
                    null),
            ],
            [
                new StructuredDispatcherExit(new StructuredBlockId(1), emptyBody),
                new StructuredDispatcherExit(new StructuredBlockId(2), populatedBody),
            ]);
        var emittedExits = new List<(StructuredSequence Body, int LoopDepth)>();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = CreateEmitter();

        EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher), dispatcher, 0),
            _ => { },
            _ => { },
            (sequence, loopDepth) => emittedExits.Add((sequence, loopDepth)));

        Assert.Equal([(populatedBody, 1)], emittedExits);
        var instructions = code.ToInstructions();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.BranchTable);
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
        var emitter = CreateEmitter();

        EmitThroughCapability(
            emitter,
            code,
            new(Method(dispatcher, CilOperation.EndFinally), dispatcher, 7),
            _ => { },
            _ => { },
            (_, _) => { });

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
        var emitter = CreateEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var dispatcher = new StructuredDispatcher(null, [], []);
        Action<StructuredDispatcherBlock> emitBlock = _ => { };
        Action<StructuredDispatcherBlock> emitCondition = _ => { };
        Action<StructuredSequence, int> emitSequence = (_, _) => { };

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
    public void ConstructorRejectsMissingBranchTablePlanner()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ControlFlowDispatcherEmitter(null!));
    }

    [Fact]
    public void RejectsNegativeProgramCounterLocalAtEachEmissionBoundary()
    {
        var emitter = CreateEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        Action<StructuredDispatcherBlock> emitBlock = _ => { };
        Action<StructuredDispatcherBlock> emitCondition = _ => { };
        Action<StructuredSequence, int> emitSequence = (_, _) => { };

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
        Action<StructuredSequence, int>? emitSequence) =>
        ((IControlFlowDispatcherEmitter)emitter).Emit(
            code!,
            request!,
            emitBlock!,
            emitCondition!,
            emitSequence!);

    private static ControlFlowDispatcherEmitter CreateEmitter() =>
        new(new DispatcherBranchTablePlanner());

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
