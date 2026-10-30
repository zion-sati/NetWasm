using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class StructuredControlFlowEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshesMetadataBeforeLoopNotOnBackedge(bool postTest)
    {
        var snapshots = new RecordingSnapshots(true);
        IStructuredControlFlowEmitter emitter = new StructuredControlFlowEmitter(
            new StructuredConditionValueEmitter(new RecordingLayoutProvider()),
            new ControlFlowDispatcherEmitter(new DispatcherBranchTablePlanner()), snapshots);
        var body = new StructuredSequence([new StructuredCode(Block(1))]);
        StructuredRegion loop = postTest
            ? new StructuredPostTestLoop(body, Block(0), true, StructuredSequence.Empty, StructuredSequence.Empty)
            : new StructuredLoop(Block(0), true, body, StructuredSequence.Empty, StructuredSequence.Empty);
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, new StructuredSequence([loop]), CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4), (_, _, _, _, _, _) => { });

        Assert.Equal([new StructuredBlockId(0)], snapshots.Entries);
        Assert.Equal(WasmOpcodes.I32Constant, code.ToArray()[0]);
        Assert.Single(code.ToInstructions(), value => value.Opcode == WasmOpcodes.Loop);
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var emitter = CreateEmitter();
        var code = new RecordingInstructionWriter();
        var sequence = StructuredSequence.Empty;
        var context = CreateContext();
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock =
            (_, _, _) => new ConditionValue(0, CliValueKind.I4);
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion = (_, _, _, _, _, _) => { };

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            null!, sequence, context, emitBlock, emitExceptionRegion));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            code, null!, context, emitBlock, emitExceptionRegion));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            code, sequence, null!, emitBlock, emitExceptionRegion));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            code, sequence, context, null!, emitExceptionRegion));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            code, sequence, context, emitBlock, null!));
    }

    [Theory]
    [InlineData(CliValueKind.ManagedReference, false, 2)]
    [InlineData(CliValueKind.ManagedReference, true, 2)]
    [InlineData(CliValueKind.ManagedAddress, false, 2)]
    [InlineData(CliValueKind.I8, false, 2)]
    [InlineData(CliValueKind.NativeInt, true, 2)]
    [InlineData(CliValueKind.NativeInt, false, 0)]
    [InlineData(CliValueKind.I4, false, 0)]
    public void ConditionalNormalizesEverySupportedConditionWidth(
        CliValueKind kind,
        bool memory64,
        int expectedEqualZeroCount)
    {
        var condition = Block(0);
        var emitted = new List<(bool IsCondition, bool IsOriginal)>();
        var code = new RecordingInstructionWriter();
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        IStructuredControlFlowEmitter emitter =
            new[] { new StructuredControlFlowEmitter(
                new StructuredConditionValueEmitter(new RecordingLayoutProvider(target)),
                new ControlFlowDispatcherEmitter(
                    new DispatcherBranchTablePlanner()), new RecordingSnapshots()) }
                .Cast<IStructuredControlFlowEmitter>()
                .Single();

        emitter.Emit(
            code,
            new StructuredSequence([
                new StructuredIf(
                    condition,
                    new StructuredSequence([new StructuredCode(Block(
                        1,
                        StructuredBlockRole.ExecutingReplica))]),
                    new StructuredSequence([new StructuredCode(Block(2))])),
            ]),
            CreateContext(),
            (block, isCondition, isOriginal) =>
            {
                emitted.Add((isCondition, isOriginal));
                return new ConditionValue(0, kind);
            },
            (_, _, _, _, _, _) => { },
            loopBreakDepth: 1,
            loopContinueDepth: 2,
            exceptionLeaveDepth: 3);

        var bytes = code.ToArray();
        Assert.Equal(3, emitted.Count);
        Assert.Equal((true, true), emitted[0]);
        Assert.Equal((false, false), emitted[1]);
        Assert.Equal((false, true), emitted[2]);
        Assert.Equal(expectedEqualZeroCount, bytes.Count(value =>
            value is WasmOpcodes.I32EqualZero or WasmOpcodes.I64EqualZero));
        Assert.Contains(WasmOpcodes.Else, bytes);
    }

    [Fact]
    public void EmptyFalseConditionalOmitsElse()
    {
        var code = new RecordingInstructionWriter();

        CreateEmitter().Emit(
            code,
            new StructuredSequence([
                new StructuredIf(Block(0), StructuredSequence.Empty, StructuredSequence.Empty),
            ]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { });

        Assert.DoesNotContain(WasmOpcodes.Else, code.ToArray());
    }

    [Fact]
    public void ConditionalPropagatesAbsentSurroundingDepthsToBothArms()
    {
        var emitted = new List<int>();

        CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            new StructuredSequence([
                new StructuredIf(
                    Block(0),
                    new StructuredSequence([new StructuredCode(Block(1))]),
                    new StructuredSequence([new StructuredCode(Block(2))])),
            ]),
            CreateContext(),
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { });

        Assert.Equal([0, 1, 2], emitted);
    }

    [Fact]
    public void ExceptionRegionEmbeddedInParentKeepsItsFallthrough()
    {
        var parent = Group();
        var child = Group();
        var sequence = new StructuredSequence(
                [new StructuredExceptionRegion(
                    child.Id,
                    new StructuredContinuationId(0))]);
        var context = CreateContext() with { ActiveExceptionGroup = parent };
        var code = new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer()));
        int? fallthrough = null;

        CreateEmitter().Emit(
            code,
            sequence,
            context,
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (exception, _, _, _, _, _) =>
            {
                fallthrough = exception.FallthroughContinuation?.Value;
            });

        Assert.Equal(0, fallthrough);
    }

    [Fact]
    public void ExceptionRegionReceivesSurroundingLoopDepths()
    {
        var group = Group();
        var sequence = new StructuredSequence(
            [new StructuredExceptionRegion(
                group.Id,
                new StructuredContinuationId(0))]);
        int? capturedBreakDepth = null;
        int? capturedContinueDepth = null;

        CreateEmitter().Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            sequence,
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, loopBreakDepth, loopContinueDepth, _, _) =>
            {
                capturedBreakDepth = loopBreakDepth;
                capturedContinueDepth = loopContinueDepth;
            },
            loopBreakDepth: 2,
            loopContinueDepth: 3);

        Assert.Equal(2, capturedBreakDepth);
        Assert.Equal(3, capturedContinueDepth);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(6)]
    public void ExceptionRegionReceivesTheSurroundingLeaveDepth(int? leaveDepth)
    {
        var group = Group();
        var sequence = new StructuredSequence(
            [new StructuredExceptionRegion(
                group.Id,
                new StructuredContinuationId(0))]);
        var captured = false;
        int? capturedLeaveDepth = -1;

        CreateEmitter().Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            sequence,
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, enclosingLeaveDepth) =>
            {
                captured = true;
                capturedLeaveDepth = enclosingLeaveDepth;
            },
            exceptionLeaveDepth: leaveDepth);

        Assert.True(captured);
        Assert.Equal(leaveDepth, capturedLeaveDepth);
    }

    [Fact]
    public void ConditionalAndLoopBreakDepthAreOwnedByStructuredEmitter()
    {
        var block = Block(0);
        var conditional = new StructuredIf(
            block,
            new StructuredSequence([new StructuredLoopBreak()]),
            StructuredSequence.Empty);
        var sequence = new StructuredSequence([conditional]);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        CreateEmitter().Emit(
            code,
            sequence,
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { },
            0);

        var bytes = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Contains(WasmOpcodes.If, bytes);
        Assert.Contains(WasmOpcodes.Branch, bytes);
        Assert.DoesNotContain(WasmOpcodes.I32EqualZero, bytes);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void PreTestLoopPreservesConditionSenseContinueAndExitSelection(
        bool continueWhenTrue,
        int expectedEqualZeroCount)
    {
        var condition = Block(0);
        var loop = new StructuredLoop(
            condition,
            continueWhenTrue,
            new StructuredSequence([new StructuredLoopContinue()]),
            StructuredSequence.Empty,
            StructuredSequence.Empty);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        CreateEmitter().Emit(
            code,
            new StructuredSequence([loop]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { });

        var bytes = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Equal(expectedEqualZeroCount, bytes.Count(value =>
            value == WasmOpcodes.I32EqualZero));
        Assert.Equal(1, CountSequence(bytes, WasmOpcodes.BranchIf, 1));
        Assert.Equal(2, CountSequence(bytes, WasmOpcodes.Branch, 0));
        Assert.Equal(0, CountSequence(bytes, WasmOpcodes.Branch, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RotatedLoopGuardsEntryAndRepeatsItsConditionAtTheBottom(bool continueWhenTrue)
    {
        var emitted = new List<(int Block, bool Owner)>();
        var code = new RecordingInstructionWriter();
        var loop = new StructuredLoop(
            Block(0),
            continueWhenTrue,
            new StructuredSequence([new StructuredCode(Block(1))]),
            new StructuredSequence([new StructuredCode(Block(2))]),
            new StructuredSequence([new StructuredCode(Block(3))]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([loop]),
            CreateContext() with { LoopRotation = LoopRotationPolicy.Guarded },
            (block, _, owner) =>
            {
                emitted.Add((block.Block.Value, owner));
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { });

        // The guard leaves when the condition fails; the bottom repeats while it holds.
        Assert.Equal([(0, true), (1, true), (2, true), (0, false), (3, true)], emitted);
        var opcodes = code.ToInstructions().Where(instruction => instruction.Opcode is not WasmOpcodes.LocalGet)
            .Select(instruction => instruction.Opcode).ToArray();
        WasmInstruction[] guard = continueWhenTrue
            ? [WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)]
            : [];
        WasmInstruction[] bottom = continueWhenTrue
            ? []
            : [WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero)];
        Assert.Equal(1, opcodes.Count(opcode => opcode == WasmOpcodes.I32EqualZero));
        Assert.Equal(2, code.ToInstructions().Count(instruction =>
            instruction == WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(0))));
        Assert.Equal(
            [WasmOpcodes.Block, WasmOpcodes.Block, .. guard.Select(instruction => instruction.Opcode), WasmOpcodes.BranchIf,
                WasmOpcodes.Loop, WasmOpcodes.Block, WasmOpcodes.End, WasmOpcodes.Block, WasmOpcodes.End,
                .. bottom.Select(instruction => instruction.Opcode), WasmOpcodes.BranchIf, WasmOpcodes.End, WasmOpcodes.End,
                WasmOpcodes.End],
            opcodes);
    }

    [Theory]
    [InlineData(CilOperation.Call, false)]
    [InlineData(CilOperation.CallVirtual, false)]
    [InlineData(CilOperation.CallIndirect, false)]
    [InlineData(CilOperation.NewObject, false)]
    [InlineData(CilOperation.CompareLessThanSigned, true)]
    public void CallFreeRotationLeavesALoopWhoseConditionCalls(CilOperation operation, bool rotated)
    {
        var emitted = new List<int>();
        var loop = new StructuredLoop(
            Block(0),
            true,
            new StructuredSequence([new StructuredCode(Block(1))]),
            StructuredSequence.Empty,
            StructuredSequence.Empty);
        var sequence = new StructuredSequence([loop]);
        var method = StructuredControlFlowEmitterTestExtensions.CreateMethod(sequence);
        var condition = method.Blocks[loop.Condition!.Block];
        method = method with
        {
            Blocks = method.Blocks.SetItem(
                condition.Id,
                condition with { Instructions = [I(0, CilOperation.LoadArgument), I(1, operation)] }),
        };

        CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            method,
            sequence,
            CreateContext() with { LoopRotation = LoopRotationPolicy.CallFree },
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { });

        // A rotated loop repeats its condition at the bottom; a calling one tests it once.
        Assert.Equal(rotated ? [0, 1, 0] : [0, 1], emitted);
    }

    [Fact]
    public void RotatedLoopKeepsBreakContinueAndSurroundingDepths()
    {
        var code = new RecordingInstructionWriter();
        StructuredSequence Transfers() => new([
            new StructuredLoopBreak(), new StructuredLoopContinue(), new StructuredDispatcherContinue(new StructuredBlockId(7))]);
        var inner = new StructuredLoop(Block(2), true, StructuredSequence.Empty, StructuredSequence.Empty, Transfers());
        var loop = new StructuredLoop(Block(0), true, new StructuredSequence([inner, .. Transfers().Regions]), Transfers(),
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([loop]),
            CreateContext() with { LoopRotation = LoopRotationPolicy.Guarded },
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { },
            exceptionLeaveDepth: 2,
            dispatcherContinueDepth: 2);

        // The inner exit body sits one block inside the outer body. In the
        // body and the continuation, break leaves the outer block, continue
        // reaches the condition and the dispatcher lies four blocks out; the
        // outer exit body reaches it through one.
        Assert.Equal(
            [4u, 1u, 7u, 3u, 0u, 6u, 3u, 0u, 6u, 3u],
            code.ToInstructions().Where(instruction => instruction.Opcode == WasmOpcodes.Branch)
                .Select(instruction => instruction.Operand.UnsignedValue));
    }

    [Fact]
    public void LabelBranchesCountTheConstructsBetweenThemAndTheirRegions()
    {
        var code = new RecordingInstructionWriter();
        var next = new StructuredBlockId(5);
        var header = new StructuredBlockId(6);
        var inner = new StructuredLoop(
            Block(2),
            true,
            new StructuredSequence([new StructuredLabelBranch(next), new StructuredLabelBranch(header)]),
            StructuredSequence.Empty,
            new StructuredSequence([new StructuredLabelBranch(next)]));
        var sequence = new StructuredSequence([
            new StructuredLabeledBlock(next, new StructuredSequence([
                new StructuredLabeledLoop(header, new StructuredSequence([
                    new StructuredIf(
                        Block(0),
                        new StructuredSequence([new StructuredLabelBranch(next)]),
                        StructuredSequence.Empty),
                    inner,
                    new StructuredLabelBranch(header)]))]))]);

        CreateEmitter().Emit(
            code,
            sequence,
            CreateContext() with { LoopRotation = LoopRotationPolicy.Guarded },
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { });

        // The if adds one level; a rotated loop's body sits four levels in and
        // its exit body one, so the block lies two, five and two out and the
        // loop four and none.
        Assert.Equal(
            [2u, 5u, 4u, 2u, 0u],
            code.ToInstructions().Where(instruction => instruction.Opcode == WasmOpcodes.Branch)
                .Select(instruction => instruction.Operand.UnsignedValue));
        Assert.Equal(
            [WasmOpcodes.Block, WasmOpcodes.Loop],
            code.ToInstructions().Where(instruction => instruction.Opcode is WasmOpcodes.Block or WasmOpcodes.Loop)
                .Take(2).Select(instruction => instruction.Opcode));
    }

    [Fact]
    public void LabelBranchOutsideItsRegionIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            new StructuredSequence([new StructuredLabelBranch(new StructuredBlockId(5))]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { }));

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void PostTestLoopPreservesConditionSenseAndEmitsEveryBody(
        bool continueWhenTrue,
        int expectedEqualZeroCount)
    {
        var emitted = new List<int>();
        var code = new RecordingInstructionWriter();
        var loop = new StructuredPostTestLoop(
            new StructuredSequence([new StructuredCode(Block(1))]),
            Block(2),
            continueWhenTrue,
            new StructuredSequence([new StructuredCode(Block(3))]),
            new StructuredSequence([new StructuredCode(Block(4))]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([loop]),
            CreateContext(),
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { },
            exceptionLeaveDepth: 2);

        Assert.Equal([1, 2, 3, 4], emitted);
        Assert.Equal(expectedEqualZeroCount, code.ToArray().Count(value =>
            value == WasmOpcodes.I32EqualZero));
        Assert.Contains(WasmOpcodes.Loop, code.ToArray());
    }

    [Fact]
    public void PreTestLoopPropagatesPresentLeaveDepthToEveryBody()
    {
        var emitted = new List<int>();
        var loop = new StructuredLoop(
            Block(0),
            true,
            new StructuredSequence([new StructuredCode(Block(1))]),
            new StructuredSequence([new StructuredCode(Block(2))]),
            new StructuredSequence([new StructuredCode(Block(3))]));

        CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            new StructuredSequence([loop]),
            CreateContext(),
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { },
            exceptionLeaveDepth: 2);

        Assert.Equal([0, 1, 2, 3], emitted);
    }

    [Fact]
    public void PostTestLoopPropagatesAbsentLeaveDepthToEveryBody()
    {
        var emitted = new List<int>();
        var loop = new StructuredPostTestLoop(
            new StructuredSequence([new StructuredCode(Block(1))]),
            Block(2),
            true,
            new StructuredSequence([new StructuredCode(Block(3))]),
            new StructuredSequence([new StructuredCode(Block(4))]));

        CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            new StructuredSequence([loop]),
            CreateContext(),
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { });

        Assert.Equal([1, 2, 3, 4], emitted);
    }

    [Fact]
    public void DispatcherEmitsConditionsBodiesExitsAndContinuation()
    {
        var emitted = new List<int>();
        var code = new RecordingInstructionWriter();
        var dispatcher = new StructuredDispatcher(
            new StructuredBlockId(1),
            [
                new StructuredDispatcherBlock(
                    Block(1),
                    new StructuredBlockId(2),
                    new StructuredBlockId(3)),
                new StructuredDispatcherBlock(
                    Block(2),
                    new StructuredBlockId(3),
                    null),
            ],
            [
                new StructuredDispatcherExit(
                    new StructuredBlockId(3),
                    new StructuredSequence([
                        new StructuredCode(Block(4)),
                        new StructuredDispatcherContinue(new StructuredBlockId(1)),
                    ])),
            ]);

        CreateEmitter().Emit(
            code,
            new StructuredSequence([dispatcher]),
            CreateContext(),
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { },
            loopBreakDepth: 2,
            loopContinueDepth: 3,
            exceptionLeaveDepth: 4);

        Assert.Contains(1, emitted);
        Assert.Contains(2, emitted);
        Assert.Contains(4, emitted);
        Assert.Contains(WasmOpcodes.Loop, code.ToArray());
    }

    [Fact]
    public void DispatcherPropagatesAbsentSurroundingDepthsToExitBody()
    {
        var emitted = new List<int>();
        var dispatcher = new StructuredDispatcher(
            null,
            [],
            [
                new StructuredDispatcherExit(
                    new StructuredBlockId(3),
                    new StructuredSequence([new StructuredCode(Block(4))])),
            ]);

        CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            new StructuredSequence([dispatcher]),
            CreateContext(),
            (block, _, _) =>
            {
                emitted.Add(block.Block.Value);
                return new ConditionValue(0, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { });

        Assert.Equal([4], emitted);
    }

    [Fact]
    public void DispatcherProvidesItsContinuationDepthToAnExceptionOccurrence()
    {
        var exception = new StructuredExceptionRegion(new(7), null);
        var dispatcher = new StructuredDispatcher(
            null,
            [],
            [
                new StructuredDispatcherExit(
                    new StructuredBlockId(3),
                    new StructuredSequence([exception])),
            ]);
        int? capturedDispatcherDepth = null;

        CreateEmitter().Emit(
            new RecordingInstructionWriter(),
            new StructuredSequence([dispatcher]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (region, _, _, _, dispatcherDepth, _) =>
            {
                Assert.Same(exception, region);
                capturedDispatcherDepth = dispatcherDepth;
            });

        Assert.Equal(1, capturedDispatcherDepth);
    }

    [Fact]
    public void DispatcherExitBodyAdjustsEverySurroundingDepthFromItsActualCase()
    {
        var exception = new StructuredExceptionRegion(new(7), null);
        var dispatcher = new StructuredDispatcher(
            null,
            [],
            [new StructuredDispatcherExit(
                new StructuredBlockId(3),
                new StructuredSequence([
                    new StructuredLoopBreak(),
                    new StructuredLoopContinue(),
                    new StructuredDispatcherContinue(new StructuredBlockId(7)),
                    exception,
                ]))]);
        var code = new RecordingInstructionWriter();
        int? capturedBreakDepth = null;
        int? capturedContinueDepth = null;
        int? capturedDispatcherDepth = null;

        CreateEmitter(new FixedDepthDispatcherEmitter(4)).Emit(
            code,
            new StructuredSequence([dispatcher]),
            CreateContext(Group()),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (region, _, breakDepth, continueDepth, dispatcherDepth, _) =>
            {
                Assert.Same(exception, region);
                capturedBreakDepth = breakDepth;
                capturedContinueDepth = continueDepth;
                capturedDispatcherDepth = dispatcherDepth;
            },
            loopBreakDepth: 2,
            loopContinueDepth: 3,
            exceptionLeaveDepth: 4);

        var instructions = code.ToInstructions();
        Assert.Contains(
            WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(8)),
            instructions);
        Assert.Contains(
            WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(9)),
            instructions);
        Assert.Contains(
            WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(4)),
            instructions);
        Assert.Contains(
            WasmInstruction.WithOperand(
                WasmOpcodes.BranchIf,
                WasmInstructionOperand.Unsigned(10)),
            instructions);
        Assert.Equal(8, capturedBreakDepth);
        Assert.Equal(9, capturedContinueDepth);
        Assert.Equal(4, capturedDispatcherDepth);
    }

    [Fact]
    public void DispatcherContinuationOutsideDispatcherFailsDeterministically()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(
                new RecordingInstructionWriter(),
                new StructuredSequence([new StructuredDispatcherContinue(
                    new StructuredBlockId(4))]),
                CreateContext(),
                (_, _, _) => new ConditionValue(0, CliValueKind.I4),
                (_, _, _, _, _, _) => { }));

        Assert.Equal(
            "structured dispatcher continuation was emitted outside a dispatcher",
            exception.Message);
    }

    [Fact]
    public void ConditionalPropagatesDispatcherDepthToBothArms()
    {
        var outputBuffer = new WasmBinaryBuffer();
        var code = new WasmInstructionWriter(new WasmBinaryWriter(outputBuffer));
        var continuation = new StructuredDispatcherContinue(new StructuredBlockId(7));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([
                new StructuredIf(
                    Block(0),
                    new StructuredSequence([continuation]),
                    new StructuredSequence([continuation])),
            ]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { },
            dispatcherContinueDepth: 2);

        Assert.Equal(
            2,
            CountSequence(
                new WasmBinarySnapshotReader(outputBuffer).Read(),
                WasmOpcodes.Branch,
                3));
    }

    [Fact]
    public void PreTestLoopPropagatesDispatcherDepthToEveryBody()
    {
        var outputBuffer = new WasmBinaryBuffer();
        var code = new WasmInstructionWriter(new WasmBinaryWriter(outputBuffer));
        var loop = new StructuredLoop(
            Block(0),
            true,
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]),
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]),
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([loop]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { },
            dispatcherContinueDepth: 2);

        var bytes = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Equal(1, CountSequence(bytes, WasmOpcodes.Branch, 6));
        Assert.Equal(1, CountSequence(bytes, WasmOpcodes.Branch, 5));
        Assert.Equal(1, CountSequence(bytes, WasmOpcodes.Branch, 3));
    }

    [Fact]
    public void PostTestLoopPropagatesDispatcherDepthToEveryBody()
    {
        var outputBuffer = new WasmBinaryBuffer();
        var code = new WasmInstructionWriter(new WasmBinaryWriter(outputBuffer));
        var loop = new StructuredPostTestLoop(
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]),
            Block(0),
            true,
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]),
            new StructuredSequence([new StructuredDispatcherContinue(new StructuredBlockId(7))]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([loop]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { },
            dispatcherContinueDepth: 2);

        var bytes = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Equal(2, CountSequence(bytes, WasmOpcodes.Branch, 6));
        Assert.Equal(1, CountSequence(bytes, WasmOpcodes.Branch, 3));
    }

    [Fact]
    public void NestedPreTestLoopExitBodyRetainsOuterLoopTargets()
    {
        var code = new RecordingInstructionWriter();
        var inner = new StructuredLoop(
            Block(2),
            true,
            new StructuredSequence([]),
            new StructuredSequence([]),
            new StructuredSequence([new StructuredLoopBreak(), new StructuredLoopContinue()]));
        var outer = new StructuredLoop(
            Block(1),
            true,
            new StructuredSequence([inner]),
            new StructuredSequence([]),
            new StructuredSequence([]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([outer]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { });

        var instructions = code.ToInstructions();
        Assert.Contains(
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(4)),
            instructions);
        Assert.Contains(
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)),
            instructions);
    }

    [Theory]
    [InlineData(false, 3u)]
    [InlineData(true, 1u)]
    public void PostTestContinuationUsesItsOwnLoopTransferDepth(bool continueLoop, uint depth)
    {
        var code = new RecordingInstructionWriter();
        StructuredRegion transfer = continueLoop
            ? new StructuredLoopContinue()
            : new StructuredLoopBreak();
        var loop = new StructuredPostTestLoop(StructuredSequence.Empty, Block(0), true,
            new StructuredSequence([transfer]), StructuredSequence.Empty);
        var emitter = CreateEmitter();

        emitter.Emit(code, new StructuredSequence([loop]), CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { }, loopBreakDepth: 7, loopContinueDepth: 8);

        Assert.Equal(new[] {
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(depth)),
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)),
        }, code.ToInstructions().Where(instruction => instruction.Opcode == WasmOpcodes.Branch));
    }

    [Theory]
    [InlineData(false, 1u)]
    [InlineData(true, 2u)]
    public void PostTestBodyContinueRestartsHeaderInsteadOfEvaluatingCondition(
        bool nestedConditional, uint depth)
    {
        var code = new RecordingInstructionWriter();
        var continuation = new StructuredSequence([new StructuredLoopContinue()]);
        var body = nestedConditional
            ? new StructuredSequence([new StructuredIf(Block(1), continuation, StructuredSequence.Empty)])
            : continuation;
        var loop = new StructuredPostTestLoop(body, Block(0), true,
            StructuredSequence.Empty, StructuredSequence.Empty);

        CreateEmitter().Emit(code, new StructuredSequence([loop]), CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { });

        Assert.Equal(new[] {
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(depth)),
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)),
        }, code.ToInstructions().Where(instruction => instruction.Opcode == WasmOpcodes.Branch));
    }

    [Fact]
    public void NestedPostTestLoopExitBodyRetainsOuterLoopTargets()
    {
        var code = new RecordingInstructionWriter();
        var inner = new StructuredPostTestLoop(
            new StructuredSequence([]),
            Block(2),
            true,
            new StructuredSequence([]),
            new StructuredSequence([new StructuredLoopBreak(), new StructuredLoopContinue()]));
        var outer = new StructuredLoop(
            Block(1),
            true,
            new StructuredSequence([inner]),
            new StructuredSequence([]),
            new StructuredSequence([]));

        CreateEmitter().Emit(
            code,
            new StructuredSequence([outer]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { });

        var instructions = code.ToInstructions();
        Assert.Contains(
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(4)),
            instructions);
        Assert.Contains(
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)),
            instructions);
    }

    [Fact]
    public void UnknownRegionFailsDeterministically()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(
                new RecordingInstructionWriter(),
                new StructuredSequence([new UnknownRegion()]),
                CreateContext(),
                (_, _, _) => new ConditionValue(0, CliValueKind.I4),
                (_, _, _, _, _, _) => { }));

        Assert.Equal("Unknown structured region UnknownRegion.", exception.Message);
    }

    [Fact]
    public void LoopBreakOutsideLoopFailsDeterministically()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(
                new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                new StructuredSequence([new StructuredLoopBreak()]),
                CreateContext(),
                (_, _, _) => new ConditionValue(0, CliValueKind.I4),
                (_, _, _, _, _, _) => { }));

        Assert.Equal(
            "structured loop break was emitted outside a loop",
            exception.Message);
    }

    [Fact]
    public void LoopContinueUsesTheStructuredContinueDepth()
    {
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        CreateEmitter().Emit(
            code,
            new StructuredSequence([new StructuredLoopContinue()]),
            CreateContext(),
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (_, _, _, _, _, _) => { },
            loopContinueDepth: 2);

        Assert.Equal(
            [WasmOpcodes.Branch, 2],
            new WasmBinarySnapshotReader(outputBuffer).Read());
    }

    [Fact]
    public void LoopContinueOutsideLoopFailsDeterministically()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(
                new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                new StructuredSequence([new StructuredLoopContinue()]),
                CreateContext(),
                (_, _, _) => new ConditionValue(0, CliValueKind.I4),
                (_, _, _, _, _, _) => { }));

        Assert.Equal(
            "structured loop continue was emitted outside a loop",
            exception.Message);
    }

    [Fact]
    public void LeaveSelectsMatchingExceptionContinuation()
    {
        var group = new StructuredExceptionGroup(
            new StructuredExceptionGroupId(0),
            null,
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuation(
                new StructuredContinuationId(0),
                42,
                new StructuredBlockId(42),
                StructuredSequence.Empty)],
            null,
            null)
        {
            ProtectedBlocks = []
        };
        var context = CreateContext(group);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        IStructuredLeaveEmitter leaves =
            new[] { new StructuredLeaveEmitter() }
                .Cast<IStructuredLeaveEmitter>()
                .Single();
        leaves.Emit(
            code,
            I(0, CilOperation.Leave, new CilOperand.BranchTarget(42)),
            new StructuredContinuationId(0),
            context);

        Assert.Equal(
            [WasmOpcodes.I32Constant, 1, WasmOpcodes.LocalSet, 11],
            new WasmBinarySnapshotReader(outputBuffer).Read());
    }

    [Fact]
    public void LeaveInsideLoopBreaksTowardExceptionCleanup()
    {
        var group = new StructuredExceptionGroup(
            new StructuredExceptionGroupId(0),
            null,
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuation(
                new StructuredContinuationId(0),
                42,
                new StructuredBlockId(42),
                StructuredSequence.Empty)],
            null,
            null)
        {
            ProtectedBlocks = []
        };
        var context = CreateContext(group);
        var leave = I(0, CilOperation.Leave, new CilOperand.BranchTarget(42));
        var block = Block(0);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        IStructuredLeaveEmitter leaves =
            new[] { new StructuredLeaveEmitter() }
                .Cast<IStructuredLeaveEmitter>()
                .Single();

        CreateEmitter().Emit(
            code,
            new StructuredSequence([new StructuredCode(block)]),
            context,
            (_, _, _) =>
            {
                leaves.Emit(code, leave, new StructuredContinuationId(0), context);
                return new ConditionValue(-1, CliValueKind.I4);
            },
            (_, _, _, _, _, _) => { },
            exceptionLeaveDepth: 2);

        Assert.Equal(
            [
                WasmOpcodes.I32Constant,
                1,
                WasmOpcodes.LocalSet,
                11,
                WasmOpcodes.LocalGet,
                11,
                WasmOpcodes.BranchIf,
                2,
            ],
            new WasmBinarySnapshotReader(outputBuffer).Read());
    }

    private static IStructuredControlFlowEmitter CreateEmitter() =>
        CreateEmitter(new ControlFlowDispatcherEmitter(
            new DispatcherBranchTablePlanner()));

    private static IStructuredControlFlowEmitter CreateEmitter(
        IControlFlowDispatcherEmitter dispatchers) =>
        new[] { new StructuredControlFlowEmitter(
            new StructuredConditionValueEmitter(new RecordingLayoutProvider()),
            dispatchers, new RecordingSnapshots()) }
            .Cast<IStructuredControlFlowEmitter>()
            .Single();

    private sealed class RecordingSnapshots(bool emitMarker = false) : IArrayMetadataSnapshotEmitter
    {
        public List<StructuredBlockId> Entries { get; } = [];
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context) =>
            throw new InvalidOperationException("Unexpected argument snapshot.");
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, ArrayReferenceSlot slot) =>
            throw new InvalidOperationException("Unexpected assignment snapshot.");
        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, StructuredBlockId loopCondition)
        {
            Entries.Add(loopCondition);
            if (emitMarker) code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(123)));
        }
    }

    private static StructuredBlockOccurrence Block(
        int index,
        StructuredBlockRole role = StructuredBlockRole.Owner) =>
        new(new StructuredBlockId(index), role);

    private static MethodEmissionContext CreateContext(
        StructuredExceptionGroup? activeGroup = null)
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var continuations = activeGroup is null
            ? ImmutableDictionary<StructuredExceptionGroupId, int>.Empty
            : ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(
                activeGroup.Id,
                11);
        return new MethodEmissionContext(
            roots,
            0,
            0,
            WasmLocalLayoutPlanner.CreateEvaluationStack(0, 1),
            6,
            7,
            8,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            continuations,
            9,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []),
            10,
            FilterEnvironmentLayout.Empty,
            0,
            12,
            13,
            14,
            15,
            16,
            17,
            ActiveExceptionGroup: activeGroup)
        {
            ManagedThrowFunction = OptionalFunctionIndex.At(7),
        };
    }

    private static StructuredExceptionGroup Group() => new(
        new StructuredExceptionGroupId(0),
        null,
        0,
        1,
        [],
        [],
        [],
        null,
        null)
    {
        ProtectedBlocks = []
    };

    private static int CountSequence(byte[] values, byte first, byte second)
    {
        var count = 0;
        for (var index = 0; index + 1 < values.Length; index++)
        {
            if (values[index] == first && values[index + 1] == second)
            {
                count++;
            }
        }
        return count;
    }

    private sealed record UnknownRegion : StructuredRegion;

    private sealed class FixedDepthDispatcherEmitter(int depth) :
        IControlFlowDispatcherEmitter
    {
        public void Emit(
            IWasmInstructionWriter code,
            ControlFlowDispatcherEmissionRequest request,
            Action<StructuredDispatcherBlock> emitBlock,
            Action<StructuredDispatcherBlock> emitCondition,
            Action<StructuredSequence, int> emitSequence) =>
            emitSequence(Assert.Single(request.Dispatcher.Exits).Body, depth);
    }
}

internal static class StructuredControlFlowEmitterTestExtensions
{
    internal static void Emit(
        this IStructuredControlFlowEmitter emitter,
        IWasmInstructionWriter code,
        StructuredSequence? sequence,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? exceptionLeaveDepth = null,
        int? dispatcherContinueDepth = null)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        var method = CreateMethod(sequence ?? StructuredSequence.Empty);
        emitter.Emit(
            code,
            method,
            sequence!,
            context,
            emitBlock,
            emitExceptionRegion,
            loopBreakDepth,
            loopContinueDepth,
            exceptionLeaveDepth,
            dispatcherContinueDepth);
    }

    internal static StructuredMethod CreateMethod(StructuredSequence sequence)
    {
        var occurrences = EnumerateOccurrences(sequence)
            .DistinctBy(occurrence => occurrence.Block)
            .ToArray();
        var blocks = occurrences.ToImmutableDictionary(
            occurrence => occurrence.Block,
            occurrence => new StructuredBlockDefinition(
                occurrence.Block,
                occurrence.Block.Value,
                [EmitterTestSupport.I(
                    occurrence.Block.Value,
                    CilOperation.Nop)],
                [],
                new StructuredFallthroughExit(null))
            {
                EndOffset = occurrence.Block.Value,
            });
        var program = new FakeProgram();
        var entryBlock = occurrences.FirstOrDefault()?.Block ?? new StructuredBlockId(0);
        return new StructuredMethod(
            new StructuredMethodHeader(
                program.GetMethod(EmitterTestSupport.EntryKey),
                null,
                0,
                [],
                [],
                []),
            entryBlock,
            blocks,
            sequence,
            [],
            ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
    }

    private static IEnumerable<StructuredBlockOccurrence> EnumerateOccurrences(
        StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    yield return code.Occurrence;
                    break;
                case StructuredIf conditional:
                    yield return conditional.Condition;
                    foreach (var occurrence in EnumerateOccurrences(conditional.WhenTrue))
                    {
                        yield return occurrence;
                    }
                    foreach (var occurrence in EnumerateOccurrences(conditional.WhenFalse))
                    {
                        yield return occurrence;
                    }
                    break;
                case StructuredLoop loop:
                    if (loop.Condition is { } condition)
                    {
                        yield return condition;
                    }
                    foreach (var occurrence in EnumerateOccurrences(loop.Body))
                    {
                        yield return occurrence;
                    }
                    foreach (var occurrence in EnumerateOccurrences(loop.ContinueBody))
                    {
                        yield return occurrence;
                    }
                    foreach (var occurrence in EnumerateOccurrences(loop.ExitBody))
                    {
                        yield return occurrence;
                    }
                    break;
                case StructuredPostTestLoop loop:
                    foreach (var occurrence in EnumerateOccurrences(loop.Body))
                    {
                        yield return occurrence;
                    }
                    yield return loop.Condition;
                    foreach (var occurrence in EnumerateOccurrences(loop.ContinueBody))
                    {
                        yield return occurrence;
                    }
                    foreach (var occurrence in EnumerateOccurrences(loop.ExitBody))
                    {
                        yield return occurrence;
                    }
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks)
                    {
                        yield return block.Occurrence;
                    }
                    foreach (var exit in dispatcher.Exits)
                    {
                        foreach (var occurrence in EnumerateOccurrences(exit.Body))
                        {
                            yield return occurrence;
                        }
                    }
                    break;
            }
        }
    }
}
