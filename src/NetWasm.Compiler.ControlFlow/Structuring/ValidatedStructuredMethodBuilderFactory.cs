using System;
using System.Collections.Immutable;
using Draft = NetWasm.Compiler.ControlFlow.Draft;
using Final = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Structuring;

public sealed class ValidatedStructuredMethodBuilderFactory : IValidatedStructuredMethodBuilderFactory
{
    private readonly IControlFlowPostDominanceAnalyzer _postDominance;
    private readonly IControlFlowComponentAnalyzer _components;
    private readonly IControlFlowNaturalLoopAnalyzer _naturalLoops;
    private readonly IReachableSetOverlapClassifier _reachableSetOverlaps;
    private readonly ILoopConditionChooser _loopConditions;
    private readonly IDispatcherBoundaryClipper _dispatcherBoundaries;

    public ValidatedStructuredMethodBuilderFactory()
        : this(
            new ControlFlowPostDominanceAnalyzer(),
            new ControlFlowComponentAnalyzer(),
            new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer()),
            new ReachableSetOverlapClassifier(),
            new LoopConditionChooser(),
            new DispatcherBoundaryClipper())
    {
    }

    public ValidatedStructuredMethodBuilderFactory(
        IControlFlowPostDominanceAnalyzer postDominance,
        IControlFlowComponentAnalyzer components,
        IControlFlowNaturalLoopAnalyzer naturalLoops,
        IReachableSetOverlapClassifier reachableSetOverlaps,
        ILoopConditionChooser loopConditions,
        IDispatcherBoundaryClipper dispatcherBoundaries)
    {
        _postDominance = postDominance ?? throw new ArgumentNullException(nameof(postDominance));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _naturalLoops = naturalLoops ?? throw new ArgumentNullException(nameof(naturalLoops));
        _reachableSetOverlaps = reachableSetOverlaps ??
            throw new ArgumentNullException(nameof(reachableSetOverlaps));
        _loopConditions = loopConditions ?? throw new ArgumentNullException(nameof(loopConditions));
        _dispatcherBoundaries = dispatcherBoundaries ??
            throw new ArgumentNullException(nameof(dispatcherBoundaries));
    }

    public IValidatedStructuredMethodBuilder Create()
    {
        var controlFlowProjector = new Final.StructuredControlFlowProjector();
        var distances = new ControlFlowDistanceFinder();
        var postDominators = new ControlFlowPostDominatorFinder(_postDominance);
        var joins = new CommonReachableBlockFinder(postDominators, distances);
        var reachableBlocks = new ReachableBlockFinder(distances);
        var errors = new IrreducibleControlFlowExceptionFactory();
        var conditionalBranches = new CilConditionalBranchClassifier();
        var wholeRegionDispatchers = new WholeRegionDispatcherBuilder(conditionalBranches);
        var exceptionAwareDispatcherBlocks = new ExceptionAwareDispatcherBlockSelector();
        var exceptionAwareDispatcherShells = new ExceptionAwareDispatcherShellBuilder(wholeRegionDispatchers, exceptionAwareDispatcherBlocks);
        var branchOverlaps = new BranchReachabilityOverlapClassifier(reachableBlocks);
        var domains = new ControlFlowDomainFinder(reachableBlocks);
        var exitExtensions = new LoopExitExtensionCollector();
        var exitPartitions = new LoopExitPartitioner(exitExtensions, _reachableSetOverlaps);
        var activeBlockRetention = new LoopActiveBlockRetentionSelector();
        var loops = new LoopRegionFactory(
            _reachableSetOverlaps,
            _loopConditions,
            joins,
            reachableBlocks,
            errors,
            exitPartitions,
            new LoopContinueTargetFinder(joins));
        var structuredControlFlow = new StructuredControlFlowBuilder(
            exceptionAwareDispatcherShells,
            new StructuredSequenceStepChain(
                [
                    new DispatcherSequenceStepExecutor(
                        new ActiveControlFlowBlockClipper(),
                        activeBlockRetention),
                    new ExceptionRegionSequenceStepExecutor(
                        _reachableSetOverlaps,
                        _dispatcherBoundaries,
                        joins,
                        reachableBlocks),
                    new BranchingSequenceStepExecutor(
                        joins,
                        reachableBlocks,
                        distances,
                        branchOverlaps,
                        new DispatcherExitSelector())]));
        var stateBuilder = new ControlFlowStructuringStateBuilder(
            _components,
            _naturalLoops,
            domains,
            loops,
            new ControlFlowCycleClassifier());
        var ranges = new BlockRangeStructurer(structuredControlFlow);
        var exceptionGroups = new ExceptionGroupStructurer(
            new ExceptionScopeFinder(),
            new ContinuationDispatcherBuilder(structuredControlFlow, reachableBlocks),
            new LoopContinuationBuilder(ranges),
            new NestedFlowClassifier(),
            ranges,
            new NormalLeaveTargetFinder());
        var finalValidator = new Final.StructuredMethodValidator(
            new Final.StructuredInstructionContractValidator(),
            new Final.StructuredBlockOwnershipValidator(),
            new Final.StructuredTargetValidator(),
            new Final.StructuredExceptionValidator(),
            new Final.StructuredStackContractValidator());
        return new ValidatedStructuredMethodBuilder(
            stateBuilder,
            exceptionGroups,
            structuredControlFlow,
            new Draft.ExceptionRegionOwnershipProjectorDraft(
                new Draft.ExceptionGroupCollector(),
                new Draft.ExceptionGroupParentMapBuilder(new ExceptionScopeFinder())),
            new Draft.StructuredControlFlowOwnershipResolverDraft(
                new Draft.StructuredControlFlowOccurrenceCollector(),
                new Draft.StructuredControlFlowOwnerSelector(),
                new Draft.StructuredControlFlowOwnershipProjector()),
            new Draft.StructuredControlFlowValidatorDraft(),
            new Final.StructuredMethodCompleter(
                new Final.StructuredMethodDraftAdapter(new Final.StructuredBlockDefinitionFactory(),
                    new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
                    new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
                    new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                        new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                            controlFlowProjector,
                new Final.StructuredExceptionGroupProjector(controlFlowProjector)),
                finalValidator));
    }
}
