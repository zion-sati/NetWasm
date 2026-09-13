using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedMethodSequenceEmitterTests
{
    [Fact]
    public void SyntheticLeaveRoutingCopiesDoNotRepeatProtectedBlockBodies()
    {
        var program = new FakeProgram();
        var method = CreateMethod(
            program,
            new StructuredLeaveExit(2, new StructuredBlockId(1)),
            I(0, CilOperation.Nop),
            I(2, CilOperation.Leave));
        method = AddBlock(
            method,
            1,
            new StructuredTerminalExit(I(3, CilOperation.Return)),
            I(3, CilOperation.Return));
        var blocks = new RecordingControlFlowEmitter
        {
            Blocks =
            [
                (Block(0, I(0, CilOperation.Nop), I(2, CilOperation.Leave)), false, false),
                (Block(0, I(0, CilOperation.Nop), I(2, CilOperation.Leave)), true, true),
                (Block(1, I(3, CilOperation.Return)), true, true),
                (Block(1, I(3, CilOperation.Return)), false, false),
                (Block(1, I(3, CilOperation.Return)), true, true),
            ],
        };
        var leaves = new RecordingLeaveEmitter();
        var dispatcher = new RecordingInstructionDispatcher();
        var emitter = AsEmitter(CreateEmitter(
            blocks,
            leaves: leaves,
            dispatcher: dispatcher));

        var result = emitter.Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Equal(1, result.OriginalBlockEmissionCounts[0]);
        Assert.Equal(2, result.OriginalBlockEmissionCounts[1]);
        Assert.Equal(4, dispatcher.Operations.Count);
        Assert.Equal(1, leaves.Calls);
        Assert.DoesNotContain(CilOperation.Branch, dispatcher.Operations);
    }

    [Fact]
    public void StructuredBranchExitIsConsumedByStructuredRouting()
    {
        var program = new FakeProgram();
        var method = CreateMethod(
            program,
            new StructuredBranchExit(1, new StructuredBlockId(1)),
            I(0, CilOperation.Nop),
            I(1, CilOperation.Branch, new CilOperand.BranchTarget(2)));
        var blocks = new RecordingControlFlowEmitter
        {
            Blocks = [(Block(0), false, true)],
        };
        var dispatcher = new RecordingInstructionDispatcher();

        AsEmitter(CreateEmitter(blocks, dispatcher: dispatcher)).Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Equal([CilOperation.Nop], dispatcher.Operations);
    }

    [Fact]
    public void EmitsPlainConditionalTerminatorAndReturnsItsConditionValue()
    {
        var program = new FakeProgram();
        var method = WithEntryStack(
            CreateMethod(
                program,
                new StructuredConditionalExit(
                    new StructuredCondition(
                        0,
                        CilOperation.BranchIfTrue,
                        0,
                        CliValueKind.I4,
                        null),
                    new StructuredBlockId(1),
                    new StructuredBlockId(1)),
                I(0, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1))),
            CliValueKind.I4);
        var blocks = new RecordingControlFlowEmitter
        {
            Blocks =
            [
                (Block(0, I(0, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1))), true, true),
            ],
        };
        var emitter = AsEmitter(CreateEmitter(blocks));

        var result = emitter.Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Equal(new ConditionValue(0, CliValueKind.I4), blocks.Conditions.Single());
        Assert.Equal(1, result.OriginalBlockEmissionCounts[0]);
    }

    [Fact]
    public void EmitsComparisonTerminatorThroughComparisonCapability()
    {
        var program = new FakeProgram();
        var method = WithEntryStack(
            CreateMethod(
                program,
                new StructuredConditionalExit(
                    new StructuredCondition(
                        0,
                        CilOperation.BranchIfEqual,
                        0,
                        CliValueKind.I4,
                        CliValueKind.I4),
                    new StructuredBlockId(1),
                    new StructuredBlockId(1)),
                I(0, CilOperation.BranchIfEqual, new CilOperand.BranchTarget(1))),
            CliValueKind.I4,
            CliValueKind.I4);
        var blocks = new RecordingControlFlowEmitter
        {
            Blocks =
            [
                (Block(0, I(0, CilOperation.BranchIfEqual, new CilOperand.BranchTarget(1))), true, true),
            ],
        };
        var comparisons = new RecordingBranchComparisonEmitter();
        var emitter = AsEmitter(CreateEmitter(
            blocks,
            branchComparisons: comparisons));

        emitter.Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Equal([CilOperation.BranchIfEqual], comparisons.Operations);
        Assert.Equal(new ConditionValue(0, CliValueKind.I4), blocks.Conditions.Single());
    }

    [Fact]
    public void RejectsConditionalTerminatorOutsideStructuredCondition()
    {
        var program = new FakeProgram();
        var method = WithEntryStack(
            CreateMethod(
                program,
                new StructuredConditionalExit(
                    new StructuredCondition(
                        0,
                        CilOperation.BranchIfTrue,
                        0,
                        CliValueKind.I4,
                        null),
                    new StructuredBlockId(1),
                    new StructuredBlockId(1)),
                I(0, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1))),
            CliValueKind.I4);
        var blocks = new RecordingControlFlowEmitter
        {
            Blocks =
            [
                (Block(0, I(0, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1))), false, true),
            ],
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AsEmitter(CreateEmitter(blocks)).Emit(
                new RecordingInstructionWriter(),
                method,
                StructuredSequence.Empty,
                CreateMethodEmissionContext(),
                CreateInstructionModuleTarget(program),
                CreateFunctionIndexResolver(program)));

        Assert.Contains("outside a structured condition", exception.Message);
    }

    [Fact]
    public void EmitsExceptionRegionAndRecursivelyEmitsItsSequence()
    {
        var program = new FakeProgram();
        var method = CreateMethod(program);
        var group = new StructuredExceptionGroupId(0);
        var blocks = new RecordingControlFlowEmitter
        {
            ExceptionGroup = group,
        };
        var exceptions = new RecordingExceptionRegionEmitter
        {
            EmitSequence = true,
        };
        var emitter = AsEmitter(CreateEmitter(blocks, exceptions: exceptions));

        emitter.Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program),
            loopBreakDepth: 2,
            loopContinueDepth: 3,
            exceptionLeaveDepth: 4);

        Assert.Equal(1, exceptions.Calls);
        Assert.Equal(2, blocks.Calls);
        Assert.Equal(4, blocks.InitialExceptionLeaveDepth);
        Assert.Equal((1, 2, 3), exceptions.SequenceDepths.Single());
    }

    [Fact]
    public void FallthroughExitCompletesWithoutEmittingAControlTransfer()
    {
        var program = new FakeProgram();
        var method = CreateMethod(program);
        var block = Assert.Single(method.Blocks.Values);
        method = method with
        {
            Header = method.Header with { Instructions = [] },
            Blocks = method.Blocks.SetItem(
                block.Id,
                block with
                {
                    Instructions = [],
                    Exit = new StructuredFallthroughExit(null),
                }),
        };
        var occurrence = new StructuredBlockOccurrence(
            method.EntryBlock,
            StructuredBlockRole.Owner);
        var controlFlow = new RecordingControlFlowEmitter
        {
            Blocks = [(occurrence, false, true)],
        };
        var result = CreateEmitter(controlFlow).Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Equal(1, result.OriginalBlockEmissionCounts[method.EntryBlock.Value]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeaveExitRequiresItsSourceInstruction(bool hasUnrelatedInstruction)
    {
        var program = new FakeProgram();
        var method = CreateMethod(program);
        var block = Assert.Single(method.Blocks.Values);
        var targetBlockId = new StructuredBlockId(method.EntryBlock.Value + 1);
        var targetBlock = block with
        {
            Id = targetBlockId,
            StartOffset = block.StartOffset + 1,
        };
        var instructions = hasUnrelatedInstruction
            ? method.Header.Instructions
            : [];
        method = method with
        {
            Header = method.Header with { Instructions = instructions },
            Blocks = method.Blocks.SetItem(
                block.Id,
                block with
                {
                    Instructions = instructions,
                    Exit = new StructuredLeaveExit(10, targetBlockId),
                })
                .Add(targetBlockId, targetBlock),
        };
        var occurrence = new StructuredBlockOccurrence(
            method.EntryBlock,
            StructuredBlockRole.Owner);
        var controlFlow = new RecordingControlFlowEmitter
        {
            Blocks = [(occurrence, false, true)],
        };
        var leaves = new RecordingLeaveEmitter();
        var emitter = CreateEmitter(
            controlFlow,
            leaves: leaves);

        var exception = Record.Exception(() => emitter.Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program)));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, leaves.Calls);
    }

    private static ManagedMethodSequenceEmitter CreateEmitter(
        RecordingControlFlowEmitter controlFlow,
        RecordingExceptionRegionEmitter? exceptions = null,
        RecordingLeaveEmitter? leaves = null,
        RecordingBranchComparisonEmitter? branchComparisons = null,
        RecordingInstructionDispatcher? dispatcher = null)
    {
        var layouts = new RecordingLayoutProvider();
        return new ManagedMethodSequenceEmitter(
            layouts,
            exceptions ?? new RecordingExceptionRegionEmitter(),
            controlFlow,
            leaves ?? new RecordingLeaveEmitter(),
            branchComparisons ?? new RecordingBranchComparisonEmitter(),
            dispatcher ?? new RecordingInstructionDispatcher());
    }

    private static IManagedMethodSequenceEmitter AsEmitter(
        IManagedMethodSequenceEmitter emitter) => emitter;

    private static StructuredMethod CreateMethod(FakeProgram program) =>
        CreateMethod(
            program,
            new StructuredTerminalExit(I(1, CilOperation.Return)),
            I(0, CilOperation.Nop),
            I(1, CilOperation.Return));

    private static StructuredMethod CreateMethod(
        FakeProgram program,
        StructuredBlockExit exit,
        params CilInstruction[] instructions)
    {
        var allInstructions = instructions.ToImmutableArray();
        var exitOffset = ExitOffset(exit);
        var blockInstructions = exitOffset is int offset
            ? allInstructions.RemoveAll(instruction => instruction.Offset == offset)
            : allInstructions;
        var definition = new StructuredBlockDefinition(
            new StructuredBlockId(0),
            allInstructions[0].Offset,
            blockInstructions,
            [],
            exit)
        {
            EndOffset = allInstructions[^1].NextOffset,
        };
        return new StructuredMethod(
            new StructuredMethodHeader(
                program.GetMethod(ConstructorKey),
                null,
                3,
                [],
                [],
                allInstructions),
            new StructuredBlockId(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(
                new StructuredBlockId(0),
                definition),
            StructuredSequence.Empty,
            [],
            ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
    }

    private static StructuredMethod AddBlock(
        StructuredMethod method,
        int index,
        StructuredBlockExit exit,
        params CilInstruction[] instructions)
    {
        var allInstructions = instructions.ToImmutableArray();
        var exitOffset = ExitOffset(exit);
        var blockInstructions = exitOffset is int offset
            ? allInstructions.RemoveAll(instruction => instruction.Offset == offset)
            : allInstructions;
        var id = new StructuredBlockId(index);
        var definition = new StructuredBlockDefinition(
            id,
            allInstructions[0].Offset,
            blockInstructions,
            [],
            exit)
        {
            EndOffset = allInstructions[^1].NextOffset,
        };
        return method with
        {
            Header = method.Header with
            {
                Instructions = [.. method.Header.Instructions, .. allInstructions],
            },
            Blocks = method.Blocks.Add(id, definition),
        };
    }

    private static StructuredMethod WithEntryStack(
        StructuredMethod method,
        params CliValueKind[] stack) => method with
        {
            Blocks = method.Blocks.SetItem(
                new StructuredBlockId(0),
                method.Blocks[new StructuredBlockId(0)] with
                {
                    EntryStack = [.. stack],
                }),
        };

    private static int? ExitOffset(StructuredBlockExit exit) => exit switch
    {
        StructuredBranchExit branch => branch.InstructionOffset,
        StructuredLeaveExit leave => leave.InstructionOffset,
        StructuredConditionalExit conditional => conditional.Condition.InstructionOffset,
        StructuredTerminalExit terminal => terminal.Instruction.Offset,
        _ => null,
    };

    private static StructuredBlockOccurrence Block(
        int index,
        params CilInstruction[] instructions) =>
        new(new StructuredBlockId(index), StructuredBlockRole.Owner);

    private sealed class RecordingControlFlowEmitter : IStructuredControlFlowEmitter
    {
        public List<(
            StructuredBlockOccurrence Block,
            bool IncludeConditional,
            bool RecordOriginal)> Blocks
        { get; init; } = [];
        public StructuredExceptionGroupId? ExceptionGroup { get; set; }
        public int Calls { get; private set; }
        public int? InitialExceptionLeaveDepth { get; private set; }
        public List<ConditionValue> Conditions { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            StructuredMethod method,
            StructuredSequence sequence,
            MethodEmissionContext context,
            Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
            Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?> emitExceptionRegion,
            int? loopBreakDepth = null,
            int? loopContinueDepth = null,
            int? exceptionLeaveDepth = null,
            int? dispatcherContinueDepth = null)
        {
            Calls++;
            InitialExceptionLeaveDepth ??= exceptionLeaveDepth;
            foreach (var item in Blocks)
            {
                Conditions.Add(emitBlock(item.Block, item.IncludeConditional, item.RecordOriginal));
            }
            if (ExceptionGroup is { } group)
            {
                ExceptionGroup = null;
                emitExceptionRegion(
                    new StructuredExceptionRegion(group, null),
                    context,
                    true,
                    loopBreakDepth,
                    loopContinueDepth,
                    dispatcherContinueDepth);
            }
        }
    }

    private sealed class RecordingExceptionRegionEmitter : IExceptionRegionEmitter
    {
        public bool EmitSequence { get; init; }
        public int Calls { get; private set; }
        public List<(int? BreakDepth, int? ContinueDepth, int? LeaveDepth)> SequenceDepths { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            StructuredMethod method,
            StructuredExceptionRegion region,
            MethodEmissionContext context,
            bool isOutermost,
            ModuleDataPlan moduleData,
            Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
            int? loopBreakDepth = null,
            int? loopContinueDepth = null,
            int? dispatcherContinueDepth = null)
        {
            Calls++;
            if (!EmitSequence)
            {
                return;
            }
            emitSequence(
                StructuredSequence.Empty,
                context,
                1,
                2,
                3);
            SequenceDepths.Add((1, 2, 3));
        }
    }

    private sealed class RecordingLeaveEmitter : IStructuredLeaveEmitter
    {
        public int Calls { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CilInstruction instruction,
            StructuredContinuationId? continuation,
            MethodEmissionContext context) => Calls++;
    }

    private sealed class RecordingBranchComparisonEmitter : IBranchComparisonEmitter
    {
        public List<CilOperation> Operations { get; } = [];

        public void Compare(
            IWasmInstructionWriter code,
            CilOperation operation,
            CliValueKind leftType,
            CliValueKind rightType) => Operations.Add(operation);
    }

    private sealed class RecordingInstructionDispatcher : ICilInstructionDispatcher
    {
        public List<CilOperation> Operations { get; } = [];

        public void Emit(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices) => Operations.Add(request.Instruction.Operation);
    }
}
