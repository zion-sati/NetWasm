using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.Devirtualization;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedMethodOptimizationPipelineTests
{
    [Fact]
    public void RevalidatesAndReappliesStaticDataOptimizationAfterInlining()
    {
        var events = new List<string>();
        var method = ValidationTestData.ManagedMethodBody();
        var first = method.Body with { MaxStack = 2 };
        var inlined = first with { MaxStack = 4 };
        var postInlining = first with { MaxStack = 5 };
        var final = first with { MaxStack = 3 };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(inlined);
        var validated = new ValidatedControlFlowGraph(
            graph,
            method.ControlFlow.EntryStacks,
            method.ControlFlow.InstructionEntryStacks);
        var optimizer = new RecordingOptimizer(events, first, postInlining);
        var inliner = new RecordingInliner(
            method.Method,
            first,
            inlined,
            events);
        var graphBuilder = new RecordingGraphBuilder(inlined, graph, events);
        var stackValidator = new RecordingStackValidator(
            graph,
            validated,
            events);
        var accessors = new RecordingCanonicalizer(
            postInlining,
            final,
            events);

        var result = new ManagedMethodOptimizationPipeline(
            optimizer,
            new NoFolding(),
            inliner,
            graphBuilder,
            stackValidator,
            accessors,
            new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds())
            .Optimize(method);

        Assert.Same(final, result);
        // Accessors are canonicalized before each round and after the last;
        // the second round finds nothing more to inline.
        Assert.Equal(
            ["optimize", "accessors", "inline", "graph", "validate", "optimize", "accessors", "inline", "accessors"],
            events);
        Assert.Equal(2, optimizer.Received.Count);
        Assert.Same(method, optimizer.Received[0]);
        Assert.Same(method.Method, optimizer.Received[1].Method);
        Assert.Same(inlined, optimizer.Received[1].Body);
        Assert.Same(validated, optimizer.Received[1].ControlFlow);
        Assert.Same(first, inliner.ReceivedBody);
        Assert.Same(method.Method, inliner.ReceivedCaller);
        Assert.Empty(inliner.ReceivedInstructionEntryStacks!);
        Assert.Equal([first, postInlining, final], accessors.Received);
    }

    [Fact]
    public void ForwardsValidatedStackFactsOnlyForTheUnchangedBody()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var inliner = new RecordingInliner(method.Method, method.Body, method.Body);
        var graphBuilder = new RecordingGraphBuilder(
            method.Body,
            method.ControlFlow.Graph);
        var stackValidator = new RecordingStackValidator(
            method.ControlFlow.Graph,
            method.ControlFlow);

        var result = new ManagedMethodOptimizationPipeline(
            new RecordingOptimizer(method.Body),
            new NoFolding(),
            inliner,
            graphBuilder,
            stackValidator,
            new RecordingCanonicalizer(method.Body, method.Body),
            new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds())
            .Optimize(method);

        Assert.Same(method.Body, result);
        Assert.Same(
            method.ControlFlow.InstructionEntryStacks,
            inliner.ReceivedInstructionEntryStacks);
        Assert.Equal(0, graphBuilder.Calls);
        Assert.Equal(0, stackValidator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentersStaticDataOptimizationBetweenBoundedInliningRounds(bool staticDataChanges)
    {
        var method = ValidationTestData.ManagedMethodBody();
        var optimizer = new GrowingOptimizer(staticDataChanges);
        var inliner = new GrowingInliner();
        var devirtualizer = new IdentityDevirtualizer();

        var result = new ManagedMethodOptimizationPipeline(
            optimizer,
            new NoFolding(),
            inliner,
            new ControlFlowGraphBuilderFactory().Create(),
            new PassThroughStackValidator(method.ControlFlow),
            new IdentityCanonicalizer(),
            new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), devirtualizer, new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds())
            .Optimize(method);

        // Each round devirtualizes before inlining, and the last round's inlined
        // calls are devirtualized once more because no round is left to do it.
        Assert.Equal(ManagedMethodOptimizationPipeline.MaximumInliningDepth + 1, devirtualizer.Calls.Count);
        Assert.Empty(devirtualizer.Calls[^1]);

        Assert.Equal(ManagedMethodOptimizationPipeline.MaximumInliningDepth, inliner.Rounds.Count);
        Assert.Equal(ManagedMethodOptimizationPipeline.MaximumInliningDepth + 1, optimizer.Calls);
        // Stack facts carry over only while static data optimization leaves the inlined body alone.
        Assert.All(inliner.Rounds.Skip(1), stacks => Assert.Equal(!staticDataChanges, ReferenceEquals(
            method.ControlFlow.InstructionEntryStacks, stacks)));
        Assert.Equal(method.Body.MaxStack + ManagedMethodOptimizationPipeline.MaximumInliningDepth * (staticDataChanges ? 2 : 1),
            result.MaxStack);
    }

    [Fact]
    public void FoldsConstantCallsBeforeEachInliningRound()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var folded = method.Body with { MaxStack = method.Body.MaxStack + 7 };
        var inliner = new RecordingInliner(method.Method, folded, folded);

        var result = new ManagedMethodOptimizationPipeline(
            new RecordingOptimizer(method.Body),
            new FixedFolding(method.Body, folded),
            inliner,
            new RecordingGraphBuilder(method.Body, method.ControlFlow.Graph),
            new RecordingStackValidator(method.ControlFlow.Graph, method.ControlFlow),
            new RecordingCanonicalizer(folded, folded),
            new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds())
            .Optimize(method);

        Assert.Same(folded, result);
        // Stack facts describe the body before folding, so none are forwarded.
        Assert.Empty(inliner.ReceivedInstructionEntryStacks!);
    }

    [Fact]
    public void ValidationFailureStopsPostInliningOptimizationAndCanonicalization()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var inlined = method.Body with { MaxStack = method.Body.MaxStack + 1 };
        var optimizer = new RecordingOptimizer(method.Body);
        var inliner = new RecordingInliner(method.Method, method.Body, inlined);
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(inlined);
        var accessors = new RecordingCanonicalizer(method.Body, method.Body);

        Assert.Throws<InvalidOperationException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer,
                new NoFolding(),
                inliner,
                new RecordingGraphBuilder(inlined, graph),
                new ThrowingStackValidator(),
                accessors,
                new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds())
                .Optimize(method));

        Assert.Single(optimizer.Received);
        // Only the canonicalization before the first round ran.
        Assert.Equal([method.Body], accessors.Received);
    }

    [Fact]
    public void RejectsMissingCapabilitiesAndMethod()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var optimizer = new RecordingOptimizer(method.Body);
        var inliner = new RecordingInliner(method.Method, method.Body, method.Body);
        var graphBuilder = new RecordingGraphBuilder(
            method.Body,
            method.ControlFlow.Graph);
        var stackValidator = new RecordingStackValidator(
            method.ControlFlow.Graph,
            method.ControlFlow);
        var accessors = new RecordingCanonicalizer(method.Body, method.Body);

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                null!, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, null!, inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), null!, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, null!, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, null!, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!, new IdentityArrayBounds()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizationPipeline(
                optimizer, new NoFolding(), inliner, graphBuilder, stackValidator, accessors, new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityDevirtualizer(), new NoFolding(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds(), new IdentityArrayBounds())
                .Optimize(null!));
    }

    private sealed class IdentityArrayBounds : IArrayBoundsCheckEliminator, IArrayLoopUnroller, IRepeatedArrayAccessEliminator,
        IRvaSpanLocalEliminator, ILocalAddressDereferenceFolder, IIdempotentLoopCollapser, IArrayLoopIdiomRewriter,
        IReductionLoopRewriter, ILoopInvariantCallHoister, IElementwiseLoopVectorizer, ILoopStoreForwarder,
        IBeforeFieldInitEntryInitializer, IEmptyFinallyRemover,
        IStructLocalScalarReplacer, NetWasm.Compiler.Analysis.Allocations.INonEscapingAllocationReplacer
    {
        public CilMethodBody Rewrite(CilMethodBody body) => body;
    }

    private sealed class IdentityDevirtualizer : IExactReceiverDevirtualizer
    {
        public List<IReadOnlyDictionary<int, ImmutableArray<CliValueKind>>> Calls { get; } = [];

        public CilMethodBody Rewrite(CilMethodBody body, IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> knownStacks)
        {
            Calls.Add(knownStacks);
            return body;
        }
    }

    private sealed class RecordingInliner(
        MethodInstanceModel expectedCaller,
        CilMethodBody expectedBody,
        CilMethodBody result,
        List<string>? events = null) : IManagedMethodInliner
    {
        public MethodInstanceModel? ReceivedCaller { get; private set; }
        public CilMethodBody? ReceivedBody { get; private set; }
        public IReadOnlyDictionary<int, ImmutableArray<CliValueKind>>?
            ReceivedInstructionEntryStacks
        { get; private set; }

        public CilMethodBody Inline(
            MethodInstanceModel caller,
            CilMethodBody body,
            IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
            bool nested)
        {
            Assert.Same(expectedCaller, caller);
            Assert.Equal(ReceivedBody is not null, nested);
            if (ReceivedBody is not null)
            {
                // A later round over the result has nothing more to inline.
                events?.Add("inline");
                return body;
            }
            Assert.Same(expectedBody, body);
            ReceivedCaller = caller;
            ReceivedBody = body;
            ReceivedInstructionEntryStacks = instructionEntryStacks;
            events?.Add("inline");
            return result;
        }
    }

    private sealed class RecordingOptimizer : IManagedMethodOptimizer
    {
        private readonly Queue<CilMethodBody> _results;
        private readonly List<string>? _events;

        public RecordingOptimizer(params CilMethodBody[] results)
            : this(null, results)
        {
        }

        public RecordingOptimizer(List<string>? events, params CilMethodBody[] results)
        {
            _events = events;
            _results = new(results);
        }

        public List<ManagedMethodBody> Received { get; } = [];

        public CilMethodBody Optimize(ManagedMethodBody method)
        {
            Received.Add(method);
            _events?.Add("optimize");
            return _results.Dequeue();
        }
    }

    // Canonicalizes the expected body to the result and leaves others as they are.
    private sealed class RecordingCanonicalizer(
        CilMethodBody expected,
        CilMethodBody result,
        List<string>? events = null) : ISimpleValueTypeAccessorCanonicalizer
    {
        public List<CilMethodBody> Received { get; } = [];

        public CilMethodBody Canonicalize(CilMethodBody body)
        {
            Received.Add(body);
            events?.Add("accessors");
            return ReferenceEquals(body, expected) ? result : body;
        }
    }

    private sealed class RecordingGraphBuilder(
        CilMethodBody expected,
        ControlFlowGraph result,
        List<string>? events = null) : IControlFlowGraphBuilder
    {
        public int Calls { get; private set; }

        public ControlFlowGraph Build(CilMethodBody methodBody)
        {
            Assert.Same(expected, methodBody);
            Calls++;
            events?.Add("graph");
            return result;
        }
    }

    private sealed class RecordingStackValidator(
        ControlFlowGraph expected,
        ValidatedControlFlowGraph result,
        List<string>? events = null) : ITypedStackValidator
    {
        public int Calls { get; private set; }

        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            Assert.Same(expected, graph);
            Calls++;
            events?.Add("validate");
            return result;
        }
    }

    private sealed class NoFolding : IMethodSpecializer
    {
        public CilMethodBody Rewrite(CilMethodBody body) => body;
    }

    private sealed class FixedFolding(CilMethodBody expected, CilMethodBody result) : IMethodSpecializer
    {
        public CilMethodBody Rewrite(CilMethodBody body) => ReferenceEquals(body, expected) ? result : body;
    }

    private sealed class GrowingInliner : IManagedMethodInliner
    {
        public List<IReadOnlyDictionary<int, ImmutableArray<CliValueKind>>> Rounds { get; } = [];

        public CilMethodBody Inline(
            MethodInstanceModel caller,
            CilMethodBody body,
            IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
            bool nested)
        {
            // Only the first round sees calls from the original body.
            Assert.Equal(Rounds.Count > 0, nested);
            Rounds.Add(instructionEntryStacks);
            return body with { MaxStack = body.MaxStack + 1 };
        }
    }

    private sealed class GrowingOptimizer(bool changes) : IManagedMethodOptimizer
    {
        public int Calls { get; private set; }

        public CilMethodBody Optimize(ManagedMethodBody method)
        {
            // The first call sees the original body, which stays unchanged.
            return Calls++ > 0 && changes ? method.Body with { MaxStack = method.Body.MaxStack + 1 } : method.Body;
        }
    }

    private sealed class PassThroughStackValidator(ValidatedControlFlowGraph facts) : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) =>
            new(graph, facts.EntryStacks, facts.InstructionEntryStacks);
    }

    private sealed class IdentityCanonicalizer : ISimpleValueTypeAccessorCanonicalizer
    {
        public CilMethodBody Canonicalize(CilMethodBody body) => body;
    }

    private sealed class ThrowingStackValidator : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) =>
            throw new InvalidOperationException();
    }
}
