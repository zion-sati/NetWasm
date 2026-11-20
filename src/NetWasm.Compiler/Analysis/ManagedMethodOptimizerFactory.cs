// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Analysis.Loops.Elementwise;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedMethodOptimizerFactory(
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilderFactory graphBuilders,
    Arrays.IArrayBoundsCheckEliminatorFactory arrayBounds,
    Arrays.IRepeatedArrayAccessEliminatorFactory repeatedAccesses,
    NullChecks.IConstructedObjectNullCheckEliminatorFactory constructedObjects,
    IIntegerCheckEliminatorFactory integerChecks) : IManagedMethodOptimizerFactory
{
    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly Arrays.IArrayBoundsCheckEliminatorFactory _arrayBounds = arrayBounds ??
        throw new ArgumentNullException(nameof(arrayBounds));
    private readonly Arrays.IRepeatedArrayAccessEliminatorFactory _repeatedAccesses = repeatedAccesses ??
        throw new ArgumentNullException(nameof(repeatedAccesses));
    private readonly NullChecks.IConstructedObjectNullCheckEliminatorFactory _constructedObjects = constructedObjects ??
        throw new ArgumentNullException(nameof(constructedObjects));
    private readonly IIntegerCheckEliminatorFactory _integerChecks = integerChecks ??
        throw new ArgumentNullException(nameof(integerChecks));

    public IManagedMethodOptimizer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        ICalledMethodResolver calledMethods,
        IRuntimeIntrinsicRegistry intrinsics,
        IMethodSpecializer constantCallFolding,
        IDispatchTargetResolver dispatchTargets,
        IFinalizerResolver finalizers,
        ITypeDefinitionResolver typeDefinitions,
        IArrayLoopMethodResolver arrayLoopMethods,
        ISpanLengthResolver spanLengths,
        IVectorLoopMethodResolver vectorLoopMethods,
        WasmOptimizationSettings settings,
        ImmutableHashSet<EntityKey>? inliningExcludedCallers = null)
    {
        ArgumentNullException.ThrowIfNull(methodBodies);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(specializer);
        ArgumentNullException.ThrowIfNull(calledMethods);
        ArgumentNullException.ThrowIfNull(intrinsics);
        ArgumentNullException.ThrowIfNull(constantCallFolding);
        ArgumentNullException.ThrowIfNull(dispatchTargets);
        ArgumentNullException.ThrowIfNull(finalizers);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(arrayLoopMethods);
        ArgumentNullException.ThrowIfNull(spanLengths);
        ArgumentNullException.ThrowIfNull(vectorLoopMethods);
        ArgumentNullException.ThrowIfNull(settings);
        var graphBuilder = _graphBuilders.Create();
        var stackValidator = _stackValidators.Create(types, fields, methods);
        var calleeBodies = new CalleeMethodBodyReader(methodBodies);
        var staticDataViews = new StaticDataViewEliminator(
            calledMethods,
            new StaticDataViewResolver(
                calleeBodies,
                specializer,
                types,
                methods),
            settings.StaticDataViewElimination);
        var dominance = new ControlFlowDominanceAnalyzer();
        var induction = new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance);
        var accessors = new SimpleValueTypeAccessorCanonicalizer(
            calleeBodies,
            calledMethods,
            fields,
            settings.AddressTakenValuePromotion,
            new MethodInitializationTriggerClassifier(types));
        var vectorLanes = new VectorLaneFields(fields, typeDefinitions);
        return new ManagedMethodOptimizationPipeline(
            staticDataViews,
            constantCallFolding,
            new ManagedMethodInliner(
                InliningResolver(new ManagedInliningResolver(
                    calleeBodies,
                    new AccessorCanonicalizingMethodSpecializer(specializer, accessors),
                    calledMethods,
                    new ManagedInliningBodyValidator(
                        graphBuilder,
                        stackValidator),
                    settings.ManagedMethodInlining,
                    new MethodInitializationTriggerClassifier(types),
                    intrinsics,
                    ManagedInliningResolver.SmallBodyInstructionLimit,
                    ManagedInliningResolver.LeafBodyInstructionLimit,
                    ManagedInliningResolver.LoopBodyInstructionLimit,
                    ManagedInliningResolver.LeafBodyILBytes), inliningExcludedCallers),
                new TerminalContinuationInliningPlanner()),
            graphBuilder,
            stackValidator,
            accessors,
            _arrayBounds.Create(graphBuilder, stackValidator, dominance, settings),
            new ArrayLoopUnroller(graphBuilder, stackValidator,
                // Bound expanded CIL work, not a presumed native cache capacity.
                new ArrayLoopUnrollingPlanner(induction, intrinsics,
                    new IntegerRangeAnalysis(new StackEffectResolver(calledMethods)), expandedBodyBudget: 128),
                settings.LoopUnrolling == LoopUnrollingPolicy.Bounded),
            _repeatedAccesses.Create(graphBuilder, stackValidator, dominance, calledMethods, settings),
            new Devirtualization.ExactReceiverDevirtualizer(calledMethods, dispatchTargets, calleeBodies, fields, graphBuilder, stackValidator,
                dominance, settings.Devirtualization == DevirtualizationPolicy.ExactType),
            new Allocations.UnusedAllocationEliminator(calleeBodies, calledMethods, finalizers,
                new MethodInitializationTriggerClassifier(types),
                settings.AllocationElimination == AllocationEliminationPolicy.Unused),
            new RvaSpanLocalEliminator(graphBuilder, dominance,
                settings.StaticDataViewElimination == StaticDataViewEliminationPolicy.Conservative),
            new LaneStoringLocalAddressDereferenceFolder(
                new ByReferenceLocalForwarder(settings.AddressTakenValuePromotion != AddressTakenValuePromotionPolicy.Disabled),
                new VectorLocalLaneStoreRewriter(graphBuilder, stackValidator, vectorLoopMethods, vectorLanes,
                    settings.AddressTakenValuePromotion != AddressTakenValuePromotionPolicy.Disabled),
                new LocalAddressDereferenceFolder(graphBuilder, stackValidator, vectorLanes,
                    settings.AddressTakenValuePromotion != AddressTakenValuePromotionPolicy.Disabled)),
            new IdempotentLoopCollapser(graphBuilder, stackValidator, intrinsics,
                settings.IdempotentLoopCollapse == IdempotentLoopCollapsePolicy.CountedLoops),
            new ArrayLoopIdiomRewriter(arrayLoopMethods, settings.ArrayLoopIdioms == ArrayLoopIdiomPolicy.Bulk),
            new ReductionLoopRewriter(calledMethods, arrayLoopMethods, spanLengths, settings.ArrayLoopIdioms == ArrayLoopIdiomPolicy.Bulk),
            new PureCallReuser(calledMethods, settings.PureCallReuse == PureCallReusePolicy.RepeatedArguments),
            new LoopInvariantCallHoister(calledMethods, settings.LoopVectorization == LoopVectorizationPolicy.CountedLoops),
            CreateElementwiseLoopVectorizer(vectorLoopMethods, calledMethods,
                settings.LoopVectorization == LoopVectorizationPolicy.CountedLoops),
            new LoopStoreForwarder(graphBuilder, stackValidator, new ControlFlowNaturalLoopAnalyzer(dominance), intrinsics,
                settings.LoopStoreForwarding == LoopStoreForwardingPolicy.Elements),
            new ConditionalStoreMerger(fields, settings.ConditionalStoreMerging == ConditionalStoreMergingPolicy.SameFieldArms),
            new FieldLoadReuser(graphBuilder, calledMethods, fields,
                new FieldStoreOwnerFinder(new StackValueConsumerFinder(new StackEffectResolver(calledMethods))),
                settings.FieldLoadReuse == FieldLoadReusePolicy.AvailableLoads),
            new QuotientReuser(graphBuilder, stackValidator, settings.QuotientReuse == QuotientReusePolicy.AvailableQuotients),
            _integerChecks.Create(graphBuilder, stackValidator, calledMethods, settings),
            new BeforeFieldInitEntryInitializer(graphBuilder, dominance, types, methods, fields,
                settings.StaticInitializationHoisting == StaticInitializationHoistingPolicy.MethodEntry),
            new EmptyFinallyRemover(settings.EmptyFinallyRemoval == EmptyFinallyRemovalPolicy.EmptyHandlers),
            new StructLocalScalarReplacer(graphBuilder, stackValidator, new ControlFlowNaturalLoopAnalyzer(dominance),
                typeDefinitions, fields, vectorLanes,
                settings.StructScalarReplacement == StructScalarReplacementPolicy.FieldAccessedLocals),
            new Allocations.NonEscapingAllocationReplacer(graphBuilder, stackValidator, calleeBodies, calledMethods, fields,
                finalizers, new MethodInitializationTriggerClassifier(types),
                new StackValueConsumerFinder(new StackEffectResolver(calledMethods)),
                settings.AllocationScalarReplacement == AllocationScalarReplacementPolicy.NonEscapingObjects),
            new WalkLoopUnroller(calledMethods, settings.LoopUnrolling == LoopUnrollingPolicy.Bounded),
            new ConstantTripLoopUnroller(settings.LoopUnrolling == LoopUnrollingPolicy.Bounded),
            new LoopInvariantExpressionHoister(calledMethods, intrinsics,
                settings.LoopInvariantExpressionHoisting == LoopInvariantExpressionHoistingPolicy.BranchingExpressions),
            new AddressInductionRewriter(calledMethods, intrinsics,
                settings.LoopStrengthReduction == LoopStrengthReductionPolicy.AddressInduction),
            new WholeLocalBlockCopyFolder(graphBuilder, stackValidator, calledMethods, typeDefinitions, fields,
                settings.AddressTakenValuePromotion != AddressTakenValuePromotionPolicy.Disabled),
            _constructedObjects.Create(graphBuilder, stackValidator, dominance, calledMethods, settings),
            new RangeBranchRewriter(graphBuilder, stackValidator,
                new IntegerRangeAnalysis(new StackEffectResolver(calledMethods)),
                Constants.ConstantBranchFoldingFactory.Create(graphBuilder),
                settings.RangeBranchFolding == RangeBranchFoldingPolicy.ProvenRanges),
            // The loops whose bounds the array passes prove are the ones that gain from it.
            new LoopArgumentLocalizer(settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative));
    }

    // Matching (the body parser, the lane selector), then rewriting (the prefixes).
    // Methods whose functions came out over an engine limit take no inlining; a compile without any keeps the
    // resolver as it is.
    private static IManagedInliningResolver InliningResolver(
        IManagedInliningResolver resolver, ImmutableHashSet<EntityKey>? excludedCallers) =>
        excludedCallers is null or { IsEmpty: true } ? resolver : new CallerExcludingInliningResolver(resolver, excludedCallers);

    private static ElementwiseLoopVectorizer CreateElementwiseLoopVectorizer(
        IVectorLoopMethodResolver vectorLoopMethods,
        ICalledMethodResolver calledMethods,
        bool enabled)
    {
        var parser = new ElementwiseBodyParser(calledMethods);
        return new ElementwiseLoopVectorizer(
            new ElementwiseLoopMatcher(parser, new ElementwiseLaneSelector()),
            new ElementwiseWalkMatcher(parser),
            new ElementwiseLoopPrefixEmitter(vectorLoopMethods),
            new ElementwiseWalkPrefixEmitter(vectorLoopMethods),
            calledMethods,
            enabled);
    }
}
