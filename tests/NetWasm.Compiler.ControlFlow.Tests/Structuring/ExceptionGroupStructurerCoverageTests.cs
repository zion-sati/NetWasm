using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionGroupStructurerCoverageTests
{
    [Fact]
    public void IrreducibleControlFlowErrorsRetainTheMethodAndReason()
    {
        var state = CreateState([]);

        var exception = new IrreducibleControlFlowExceptionFactory()
            .Create(state, "broken flow");

        Assert.Equal(DiagnosticCode.IrreducibleControlFlow, exception.Diagnostic.Code);
        Assert.Contains("broken flow", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Method", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildsNestedProtectedGroupsAndDispatchedContinuations(
        bool targetAfterBoundary)
    {
        var regions = ImmutableArray.Create(
            new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 4, 4, 1, null, null),
            new CilExceptionRegion(CilExceptionRegionKind.Finally, 1, 1, 2, 1, null, null),
            new CilExceptionRegion(CilExceptionRegionKind.Finally, 4, 1, 4, 1, null, null),
            new CilExceptionRegion(CilExceptionRegionKind.Finally, 6, 1, 7, 1, null, null));
        var state = CreateState(regions);
        var targets = targetAfterBoundary ? new[] { 5, 7 } : new[] { 5, 6 };
        var dispatchers = new RecordingContinuationDispatcherBuilder();
        var structurer = new ExceptionGroupStructurer(
            new ExceptionScopeFinder(),
            dispatchers,
            new UnusedLoopContinuationBuilder(),
            new NonNestedFlowClassifier(),
            new EmptyBlockRangeStructurer(),
            new FixedNormalLeaveTargetFinder(targets));

        var groups = structurer.Structure(state, regions);

        var parent = groups[0];
        Assert.NotNull(parent.ContinuationDispatcher);
        Assert.Equal(targets, dispatchers.Targets);
        Assert.All(parent.NormalContinuations, continuation =>
            Assert.Empty(continuation.Body.Regions));
        Assert.Equal(targetAfterBoundary, parent.ContinuationJoinBlock is null);
    }

    private static ControlFlowStructuringState CreateState(
        ImmutableArray<CilExceptionRegion> regions)
    {
        var instructions = Enumerable.Range(0, 10)
            .Select(offset => new CilInstruction(
                offset,
                offset + 1,
                offset == 9 ? CilOperation.Return : CilOperation.Nop,
                new CilOperand.None()))
            .ToImmutableArray();
        var method = new MethodDefinitionModel(
            new EntityKey(default, 1),
            new EntityKey(default, 2),
            "Method",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var body = new CilMethodBody(method, 0, [], instructions)
        {
            ExceptionRegions = regions,
        };
        var blocks = instructions
            .Select((instruction, index) => new BasicBlock(index, index, [instruction]))
            .ToImmutableArray();
        var emptyEdges = blocks.ToImmutableDictionary(
            block => block.Index,
            _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(
            body,
            blocks,
            emptyEdges,
            emptyEdges,
            emptyEdges,
            emptyEdges,
            blocks.Select(block => block.Index).ToImmutableHashSet());
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    private sealed class RecordingContinuationDispatcherBuilder :
        IContinuationDispatcherBuilder
    {
        public int[] Targets { get; private set; } = [];

        public StructuredDispatcherDraft Build(
            ControlFlowStructuringState state,
            IEnumerable<int> targets,
            int continuationBoundary,
            int methodEnd)
        {
            Targets = targets.ToArray();
            return new StructuredDispatcherDraft(null, [], []);
        }
    }

    private sealed class UnusedLoopContinuationBuilder : ILoopContinuationBuilder
    {
        public StructuredSequenceDraft Build(
            ControlFlowStructuringState state,
            int offset,
            int length,
            LoopRegion loop) => throw new InvalidOperationException();
    }

    private sealed class NonNestedFlowClassifier : INestedFlowClassifier
    {
        public bool Classify(
            ControlFlowStructuringState state,
            ExceptionGroupSource source,
            ExceptionGroupSource[] children) => false;
    }

    private sealed class EmptyBlockRangeStructurer : IBlockRangeStructurer
    {
        public StructuredSequenceDraft Structure(
            ControlFlowStructuringState state,
            int offset,
            int length) => StructuredSequenceDraft.Empty;
    }

    private sealed class FixedNormalLeaveTargetFinder(int[] targets) :
        INormalLeaveTargetFinder
    {
        public int[] Find(
            ControlFlowStructuringState state,
            IEnumerable<CilExceptionRegion> regions,
            IReadOnlyCollection<ExceptionGroupSource> excluded,
            IReadOnlyCollection<ExceptionGroupSource> handlerExcluded) =>
            regions.First().TryOffset == 0 ? targets : [];
    }
}
