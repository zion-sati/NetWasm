using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Analysis.StaticArrays;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis;

internal sealed class WholeProgramAnalyzerFactory(
    ICalledMethodResolverFactory calledMethods,
    ITypeOperandResolverFactory typeOperands,
    IMethodSpecializerFactory specializers,
    IManagedMethodOptimizerFactory optimizers,
    IAttributeQueryMethodSpecializerFactory attributeQueries,
    IDispatchTargetResolverFactory dispatchTargets,
    ITypeRelationshipClassifierFactory typeRelationships,
    ITypeRelationshipKeyResolverFactory typeRelationshipKeys,
    IDelegateTypeRecognizerFactory delegateTypes,
    IReachabilityImportClassifierFactory importClassifiers,
    IReachabilityInstructionAnalyzerFactory instructionAnalyzers,
    IReachableMethodAnalyzerFactory methodAnalyzers,
    ITypeTestPlannerFactory typeTestPlanners,
    IImplicitExceptionDiscoveryFactory exceptionDiscoveries,
    IReachableProgramBuilderFactory programBuilders,
    IReachabilityLedgerFactory ledgers,
    IAllocationCapabilityAnalyzerFactory allocationCapabilities,
    IManagedByRefEffectAnalyzerFactory byRefEffects,
    IBaseTypeResolverFactory baseTypeResolvers,
    IDelegateMethodClassifierFactory delegateMethodClassifiers,
    IDispatchSiteKeyBuilder dispatchSiteKeys,
    IDelegateBindingPlannerFactory delegateBindingPlanners,
    IManagedCallSiteLedgerWriter managedCallSites,
    INullableTypeResolver nullableTypes,
    IReachableMethodBatchObserver reachableMethodBatchObserver,
    IReachabilityClosureObserver reachabilityClosureObserver,
    ITypeClassifierFactory typeClassifiers,
    IControlFlowGraphBuilderFactory graphBuilders) : IWholeProgramAnalyzerFactory
{
    public IWholeProgramAnalyzer Create(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fieldRepository,
        IMethodRepository methodRepository,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver typeIdentities,
        IBaseTypeIdentityResolver baseTypeIdentities,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodBodyReader methodBodies,
        IMethodInstanceResolver methodInstances,
        IMethodImplementationResolver methodImplementations,
        ISymbolFormatter symbols,
        Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> javaScriptAsyncBindings,
        IRuntimeIntrinsicRegistry intrinsics,
        WasmOptimizationSettings optimizationSettings,
        PlatformWorldCapabilities world)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(typeRepository);
        ArgumentNullException.ThrowIfNull(fieldRepository);
        ArgumentNullException.ThrowIfNull(methodRepository);
        ArgumentNullException.ThrowIfNull(typeFinder);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(typeIdentities);
        ArgumentNullException.ThrowIfNull(baseTypeIdentities);
        ArgumentNullException.ThrowIfNull(implementedInterfaces);
        ArgumentNullException.ThrowIfNull(methodBodies);
        ArgumentNullException.ThrowIfNull(methodInstances);
        ArgumentNullException.ThrowIfNull(methodImplementations);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(javaScriptAsyncBindings);
        ArgumentNullException.ThrowIfNull(intrinsics);
        ArgumentNullException.ThrowIfNull(optimizationSettings);
        ArgumentNullException.ThrowIfNull(world);
        if (optimizationSettings.ConstantArgumentPropagation is not (ConstantArgumentPropagationPolicy.Disabled or
            ConstantArgumentPropagationPolicy.Conservative) ||
            optimizationSettings.StaticArrayPopulationElimination is not (StaticArrayPopulationEliminationPolicy.Disabled or
            StaticArrayPopulationEliminationPolicy.Conservative) ||
            optimizationSettings.ConstantCallFolding is not (ConstantCallFoldingPolicy.Disabled or
            ConstantCallFoldingPolicy.Conservative) ||
            optimizationSettings.SwitchBitTests is not (SwitchBitTestPolicy.Disabled or
            SwitchBitTestPolicy.Conservative) ||
            optimizationSettings.Devirtualization is not (DevirtualizationPolicy.Disabled or
            DevirtualizationPolicy.ExactType) ||
            optimizationSettings.AllocationElimination is not (AllocationEliminationPolicy.Disabled or
            AllocationEliminationPolicy.Unused) ||
            optimizationSettings.UnwrittenStaticFieldFolding is not (UnwrittenStaticFieldFoldingPolicy.Disabled or
            UnwrittenStaticFieldFoldingPolicy.WholeProgram))
            throw new ArgumentOutOfRangeException(nameof(optimizationSettings));
        var parts = MethodAnalysisParts.Build(
            new(calledMethods, typeOperands, specializers, optimizers, attributeQueries, dispatchTargets,
                typeRelationships, typeRelationshipKeys, delegateTypes, instructionAnalyzers, methodAnalyzers,
                exceptionDiscoveries, baseTypeResolvers, delegateMethodClassifiers, dispatchSiteKeys, graphBuilders),
            metadata, typeRepository, fieldRepository, methodRepository, typeFinder, typeDefinitions, typeIdentities,
            baseTypeIdentities, implementedInterfaces, methodBodies, methodInstances, methodImplementations, symbols,
            intrinsics, optimizationSettings, world);
        var baseTypes = parts.BaseTypes;
        var relationships = parts.Relationships;
        var relationshipKeys = parts.RelationshipKeys;
        var delegateTypeRecognizer = parts.DelegateTypeRecognizer;
        var calls = parts.Calls;
        var types = parts.Types;
        IMethodRewriteRule ConstantCalls() => parts.ConstantCalls();
        var constantCallFolding = parts.ConstantCallFolding;
        var reducers = ImmutableArray.CreateBuilder<IReachableProgramReducer>();
        if (optimizationSettings.ConstantArgumentPropagation == ConstantArgumentPropagationPolicy.Conservative)
        {
            reducers.Add(new ConstantArgumentProgramReducer(
                new CallArgumentConstantCollector(calls),
                new MethodSpecializer(
                [
                    ConstantCalls(),
                    new KnownSizeRewriter(calls),
                    ConstantBranchFoldingFactory.Create(graphBuilders.Create()),
                ])));
        }
        if (constantCallFolding is not null)
        {
            reducers.Add(new ConstantCallProgramReducer(constantCallFolding));
        }
        if (optimizationSettings.StaticArrayPopulationElimination == StaticArrayPopulationEliminationPolicy.Conservative)
        {
            reducers.Add(new StaticArrayPopulationReducer(
                    new StaticFieldObservationCollector(),
                    new StaticArrayPopulationPlanner(fieldRepository, calls, types,
                        new ValueConstructorPurityClassifier(methodBodies, fieldRepository, typeDefinitions, methodRepository, intrinsics),
                        new ClosedTypeIdentityComparer(typeDefinitions), typeDefinitions)));
        }
        if (optimizationSettings.UnwrittenStaticFieldFolding == UnwrittenStaticFieldFoldingPolicy.WholeProgram)
        {
            reducers.Add(new UnwrittenStaticFieldProgramReducer(
                new StaticFieldWriteCollector(fieldRepository),
                fieldRepository,
                typeIdentities,
                typeDefinitions,
                methodRepository,
                new MethodSpecializer(
                [
                    ConstantCalls(),
                    new KnownSizeRewriter(calls),
                    ConstantBranchFoldingFactory.Create(graphBuilders.Create()),
                ])));
        }
        var concurrency = SelectConcurrency(
            OperatingSystem.IsBrowser(), Environment.ProcessorCount);
        var methodAnalyzerWorkers =
            System.Collections.Immutable.ImmutableArray
                .CreateBuilder<IReachableMethodAnalyzer>(concurrency.WorkerCount + 1);
        // A reducing closure builds the program again after each reduction, so the analyzers
        // share a memo and analyze again only the bodies a reduction rewrote.
        var memo = reducers.Count == 0 ? null : new ReachableMethodAnalysisMemo();
        for (var workerIndex = 0; workerIndex < concurrency.AnalyzerCount; workerIndex++)
            methodAnalyzerWorkers.Add(memo is null
                ? parts.CreateAnalyzer()
                : new MemoizingReachableMethodAnalyzer(parts.CreateAnalyzer(), memo));
        var methodAnalyzerBatch = methodAnalyzerWorkers.ToImmutable();
        var methodBatchAnalyzer = CreateMethodAnalysisSessions(
            concurrency.Synchronous, methodAnalyzerBatch,
            reachableMethodBatchObserver);
        var asyncBindingResolver = new DelegateJavaScriptAsyncBindingResolver(
            javaScriptAsyncBindings);
        var importClassifier = importClassifiers.Create(
            typeFinder,
            typeDefinitions,
            methodRepository,
            delegateTypeRecognizer,
            asyncBindingResolver,
            new JavaScriptConversionResolver(typeFinder, methodRepository, methodInstances, symbols),
            intrinsics,
            symbols);
        var programBuilder = programBuilders.Create(
            allocationCapabilities.Create(
                typeRepository,
                fieldRepository,
                methodRepository,
                intrinsics,
                typeClassifiers.Create(metadata)),
            byRefEffects.Create(methodRepository, intrinsics),
            delegateBindingPlanners.Create(relationships));
        var closure = new ReachabilityClosureBuilder(
            typeRepository,
            fieldRepository,
            methodRepository,
            typeFinder,
            typeDefinitions,
            typeIdentities,
            methodInstances,
            symbols,
            dispatchTargets.Create(
                typeFinder,
                typeDefinitions,
                methodRepository,
                methodImplementations,
                implementedInterfaces,
                methodInstances,
                symbols,
                relationships,
                baseTypes),
            delegateTypeRecognizer,
            importClassifier,
            methodBatchAnalyzer,
            typeTestPlanners.Create(types, relationships, relationshipKeys),
            programBuilder,
            ledgers,
            managedCallSites,
            new RuntimeIntrinsicTypeRootPlanner(intrinsics, nullableTypes, typeDefinitions),
            reachabilityClosureObserver,
            new DispatchCandidateIndexFactory(relationships, relationshipKeys),
            new ModuleInitializerResolver(
                metadata.Types,
                metadata.Methods),
            new ModuleInitializerOrderer(BuildModuleDependencies(metadata)),
            new MemberDescriptorPlanner(new MetadataPropertyAccessorResolver(
                [.. metadata.Assemblies.SelectMany(assembly =>
                    assembly.Properties.Values)],
                methodInstances)),
            new ObjectArrayDelegateAdapterPlanner(
                intrinsics,
                delegateTypeRecognizer,
                typeFinder,
                typeDefinitions,
                methodRepository,
                methodInstances,
                symbols),
            new MemberExecutionPlanner(
                intrinsics,
                delegateTypeRecognizer,
                typeFinder,
                methodRepository,
                methodInstances,
                symbols,
                nullableTypes),
            new FinalizerResolver(
                typeDefinitions,
                methodRepository,
                baseTypes),
            nullableTypes,
            new MethodInitializationTriggerClassifier(typeRepository),
            new IntrinsicAddressBodyPlanner(intrinsics),
            new RuntimeTypeFactsClosurePlanner(baseTypes, typeDefinitions, typeIdentities));
        return new WholeProgramAnalyzer(reducers.Count == 0
            ? closure
            : new ReducingReachabilityClosureBuilder(closure,
                new ReachableProgramReductionChain(reducers.ToImmutable())));
    }

    internal static AnalysisConcurrency SelectConcurrency(
        bool browserHost, int processorCount) => browserHost
        ? new(1, true)
        : new(processorCount, false);

    // The last analyzer is the closure's own; the rest are the pool's workers.
    internal static IReachableMethodAnalysisSessionFactory CreateMethodAnalysisSessions(
        bool synchronous,
        ImmutableArray<IReachableMethodAnalyzer> analyzers,
        IReachableMethodBatchObserver observer) => synchronous || analyzers.Length == 1
        ? new SynchronousReachableMethodAnalysisSessionFactory(analyzers[0], observer)
        : new PooledReachableMethodAnalysisSessionFactory(analyzers[..^1], analyzers[^1], observer);

    private static Dictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>>
        BuildModuleDependencies(MetadataCompilationSnapshot metadata)
    {
        var aliases = metadata.ReferenceAssemblyAliases ??
            ImmutableDictionary<string, string>.Empty;
        return metadata.Assemblies.ToDictionary(
            assembly => assembly.Identity,
            assembly => (IReadOnlyList<AssemblyIdentity>)[
                .. assembly.References.Select(reference => new AssemblyIdentity(
                    aliases.TryGetValue(reference, out var alias) ? alias : reference))
            ]);
    }
}

internal readonly record struct AnalysisConcurrency(
    int WorkerCount,
    bool Synchronous)
{
    // One analyzer per worker and one for the closure's own thread; a synchronous closure
    // analyzes on its own thread alone.
    public int AnalyzerCount => Synchronous ? 1 : WorkerCount + 1;
}

internal sealed class DelegateJavaScriptAsyncBindingResolver(
    Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> resolve) :
    IJavaScriptAsyncBindingResolver
{
    private readonly Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> _resolve =
        resolve ?? throw new ArgumentNullException(nameof(resolve));

    public JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method) =>
        _resolve(method);
}
