using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.Loops;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedMethodOptimizerFactory(
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilderFactory graphBuilders) : IManagedMethodOptimizerFactory
{
    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));

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
        IVectorLoopMethodResolver vectorLoopMethods,
        WasmOptimizationSettings settings)
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
        ArgumentNullException.ThrowIfNull(vectorLoopMethods);
        ArgumentNullException.ThrowIfNull(settings);
        var graphBuilder = _graphBuilders.Create();
        var stackValidator = _stackValidators.Create(types, fields, methods);
        var staticDataViews = new StaticDataViewEliminator(
            calledMethods,
            new StaticDataViewResolver(
                methodBodies,
                specializer,
                types,
                methods),
            settings.StaticDataViewElimination);
        var dominance = new ControlFlowDominanceAnalyzer();
        var induction = new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance);
        var accessors = new SimpleValueTypeAccessorCanonicalizer(
            methodBodies,
            calledMethods,
            fields,
            settings.AddressTakenValuePromotion,
            new MethodInitializationTriggerClassifier(types));
        return new ManagedMethodOptimizationPipeline(
            staticDataViews,
            constantCallFolding,
            new ManagedMethodInliner(
                new ManagedInliningResolver(
                    methodBodies,
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
                    ManagedInliningResolver.LoopBodyInstructionLimit),
                new TerminalContinuationInliningPlanner()),
            graphBuilder,
            stackValidator,
            accessors,
            new ArrayBoundsCheckEliminator(graphBuilder, stackValidator,
                new ArrayBoundsCheckEliminationPlanner(
                    induction, dominance),
                settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative),
            new ArrayLoopUnroller(graphBuilder, stackValidator,
                // Bound expanded CIL work, not a presumed native cache capacity.
                new ArrayLoopUnrollingPlanner(induction, expandedBodyBudget: 128),
                settings.LoopUnrolling == LoopUnrollingPolicy.Bounded),
            new RepeatedArrayAccessEliminator(graphBuilder, stackValidator,
                new RepeatedArrayAccessPlanner(dominance),
                settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative),
            new Devirtualization.ExactReceiverDevirtualizer(calledMethods, dispatchTargets, methodBodies, fields, graphBuilder, stackValidator,
                dominance, settings.Devirtualization == DevirtualizationPolicy.ExactType),
            new Allocations.UnusedAllocationEliminator(methodBodies, calledMethods, finalizers,
                settings.AllocationElimination == AllocationEliminationPolicy.Unused),
            new RvaSpanLocalEliminator(graphBuilder, dominance,
                settings.StaticDataViewElimination == StaticDataViewEliminationPolicy.Conservative),
            new LocalAddressDereferenceFolder(graphBuilder, stackValidator,
                settings.AddressTakenValuePromotion != AddressTakenValuePromotionPolicy.Disabled),
            new IdempotentLoopCollapser(graphBuilder, stackValidator, intrinsics,
                settings.IdempotentLoopCollapse == IdempotentLoopCollapsePolicy.CountedLoops),
            new ArrayLoopIdiomRewriter(arrayLoopMethods, settings.ArrayLoopIdioms == ArrayLoopIdiomPolicy.Bulk),
            new ReductionLoopRewriter(calledMethods, arrayLoopMethods, settings.ArrayLoopIdioms == ArrayLoopIdiomPolicy.Bulk),
            new LoopInvariantCallHoister(calledMethods, settings.LoopVectorization == LoopVectorizationPolicy.CountedLoops),
            new ElementwiseLoopVectorizer(vectorLoopMethods, calledMethods,
                settings.LoopVectorization == LoopVectorizationPolicy.CountedLoops),
            new LoopStoreForwarder(graphBuilder, stackValidator, new ControlFlowNaturalLoopAnalyzer(dominance), intrinsics,
                settings.LoopStoreForwarding == LoopStoreForwardingPolicy.Elements),
            new BeforeFieldInitEntryInitializer(graphBuilder, dominance, types, methods, fields,
                settings.StaticInitializationHoisting == StaticInitializationHoistingPolicy.MethodEntry),
            new EmptyFinallyRemover(settings.EmptyFinallyRemoval == EmptyFinallyRemovalPolicy.EmptyHandlers),
            new StructLocalScalarReplacer(graphBuilder, stackValidator, new ControlFlowNaturalLoopAnalyzer(dominance),
                typeDefinitions, fields,
                settings.StructScalarReplacement == StructScalarReplacementPolicy.FieldAccessedLocals),
            new Allocations.NonEscapingAllocationReplacer(graphBuilder, stackValidator, methodBodies, calledMethods, fields,
                finalizers, new MethodInitializationTriggerClassifier(types),
                new StackValueConsumerFinder(new StackEffectResolver(calledMethods)),
                settings.AllocationScalarReplacement == AllocationScalarReplacementPolicy.NonEscapingObjects));
    }
}
