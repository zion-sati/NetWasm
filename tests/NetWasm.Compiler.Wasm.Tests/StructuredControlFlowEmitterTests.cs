using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StructuredControlFlowEmitterTests
{
    [Fact]
    public void RejectsMissingCollaborators()
    {
        var emitter = CreateEmitter();
        var code = new RecordingInstructionWriter();
        var sequence = StructuredSequence.Empty;
        var context = CreateContext();
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock =
            (_, _, _) => new ConditionValue(0, CliValueKind.I4);
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
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
                new RecordingLayoutProvider(target),
                new ControlFlowDispatcherEmitter()) }
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
    public void ExceptionRegionEmbeddedInParentIsNotEmittedAsOutermost()
    {
        var parent = Group();
        var child = Group();
        var sequence = new StructuredSequence(
                [new StructuredExceptionRegion(
                    child.Id,
                    new StructuredContinuationId(0))]);
        var context = CreateContext() with { ActiveExceptionGroup = parent };
        var code = new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer()));
        bool? isOutermost = null;
        int? fallthrough = null;

        CreateEmitter().Emit(
            code,
            sequence,
            context,
            (_, _, _) => new ConditionValue(0, CliValueKind.I4),
            (exception, _, outermost, _, _, _) =>
            {
                isOutermost = outermost;
                fallthrough = exception.FallthroughContinuation?.Value;
            });

        Assert.False(isOutermost);
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
            (_, _, _, loopBreakDepth, loopContinueDepth, _) =>
            {
                capturedBreakDepth = loopBreakDepth;
                capturedContinueDepth = loopContinueDepth;
            },
            loopBreakDepth: 2,
            loopContinueDepth: 3);

        Assert.Equal(2, capturedBreakDepth);
        Assert.Equal(3, capturedContinueDepth);
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
            (region, _, _, _, _, dispatcherDepth) =>
            {
                Assert.Same(exception, region);
                capturedDispatcherDepth = dispatcherDepth;
            });

        Assert.Equal(1, capturedDispatcherDepth);
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
        new[] { new StructuredControlFlowEmitter(
            new RecordingLayoutProvider(),
            new ControlFlowDispatcherEmitter()) }
            .Cast<IStructuredControlFlowEmitter>()
            .Single();

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
            ActiveExceptionGroup: activeGroup);
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
}

internal static class StructuredControlFlowEmitterTestExtensions
{
    internal static void Emit(
        this IStructuredControlFlowEmitter emitter,
        IWasmInstructionWriter code,
        StructuredSequence? sequence,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
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

    private static StructuredMethod CreateMethod(StructuredSequence sequence)
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
                    yield return loop.Condition;
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
