// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
using NetWasm.Compiler.Analysis.DynamicCasts;
using NetWasm.Compiler.Analysis.Reachability;
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
    IReachabilityProgressObserver reachabilityProgressObserver,
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
        PlatformWorldCapabilities world,
        ImmutableHashSet<EntityKey>? inliningExcludedCallers = null)
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
            optimizationSettings.ConditionalSelects is not (ConditionalSelectPolicy.Disabled or
            ConditionalSelectPolicy.Enabled) ||
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
            intrinsics, optimizationSettings, world, inliningExcludedCallers);
        var baseTypes = parts.BaseTypes;
        var relationships = parts.Relationships;
        var relationshipKeys = parts.RelationshipKeys;
        var delegateTypeRecognizer = parts.DelegateTypeRecognizer;
        var calls = parts.Calls;
        var types = parts.Types;
        IMethodRewriteRule ConstantCalls() => parts.ConstantCalls();
        var constantCallFolding = parts.ConstantCallFolding;
        var reducers = ImmutableArray.CreateBuilder<IReachableProgramReducer>();
        // A reducing closure builds the program again after each reduction, so the analyzers
        // share a memo and analyze again only the bodies a reduction rewrote.
        var reducerMemo = new ReachableMethodAnalysisMemo();
        // The reductions that reach new code run first in every round, so the program has stopped growing before an
        // optimization rewrites it on a fact that holds only for the whole program: a constant every call site passes
        // (ConstantArgumentProgramReducer), a field nothing writes or an array nothing reads. A call site, write or read
        // that a later round reached would find a body specialized without it; Type.HasFlag specialized to the one
        // flag its callers had passed answered IsArray with another flag once IDynamicInterfaceCastable's helpers read
        // GetElementType.
        var growing = ImmutableArray.CreateBuilder<IReachableProgramReducer>();
        // Correctness, not an optimization: every preset answers a query on a Type known only at run time. The answer
        // follows the reductions' rewrites, which can change the program's run-time types: each round lowers the query
        // again.
        growing.Add(parts.RuntimeTypeAttributeQueries);
        // Correctness too: a program that allocates a class implementing IDynamicInterfaceCastable has its interface
        // casts, array stores and calls ask such an object, as .NET's runtime does.
        if (metadata.Types.FirstOrDefault(type => type.FullName == "System.Runtime.InteropServices.IDynamicInterfaceCastable" &&
                type.Key.Assembly == typeFinder.FindType("System.Object").Key.Assembly) is { } castableType)
        {
            var castable = typeIdentities.GetTypeIdentity(castableType.Key);
            growing.Add(new DynamicInterfaceCastableProgramReducer(parts.Relationships, calls, symbols,
                new DynamicCastBodyRewriter(types, calls, typeDefinitions,
                    new DynamicImplementationInterfaceFinder(metadata, typeIdentities, implementedInterfaces,
                        new CustomAttributeDescriptorReaderFactory().Create(metadata, typeDefinitions, typeIdentities),
                        parts.Relationships),
                    parts.DispatchTargets,
                    new DynamicCastHelperResolver(typeFinder, methodRepository, methodInstances, symbols),
                    castable),
                castable));
        }
        if (optimizationSettings.ConstantArgumentPropagation == ConstantArgumentPropagationPolicy.Conservative)
        {
            reducers.Add(new ConstantArgumentProgramReducer(
                new CallArgumentConstantCollector(calls),
                new MethodSpecializer(
                [
                    ConstantCalls(),
                    new KnownSizeRewriter(calls, world.NativeIntegerSize),
                    ConstantBranchFoldingFactory.Create(graphBuilders.Create()),
                ]),
                new MemoizingReachableMethodAnalyzer(parts.CreateAnalyzer(), reducerMemo)));
        }
        if (constantCallFolding is not null)
        {
            reducers.Add(new ConstantCallProgramReducer(constantCallFolding));
        }
        if (optimizationSettings.StaticArrayPopulationElimination == StaticArrayPopulationEliminationPolicy.Conservative)
        {
            var initialization = new ValueTypeInitializationClassifier(typeDefinitions, methodRepository);
            reducers.Add(new StaticArrayPopulationReducer(
                    new StaticFieldObservationCollector(),
                    new StaticArrayPopulationPlanner(fieldRepository, calls, types,
                        new ValueConstructorPurityClassifier(new CalleeMethodBodyReader(methodBodies), fieldRepository, intrinsics,
                            initialization),
                        new ClosedTypeIdentityComparer(typeDefinitions), typeDefinitions, initialization)));
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
                    new KnownSizeRewriter(calls, world.NativeIntegerSize),
                    ConstantBranchFoldingFactory.Create(graphBuilders.Create()),
                ])));
        }
        // Only optimizations analyze bodies again so often that every analysis is worth keeping: a compile without one
        // keeps no memo, as before, and analyzes again only if a run-time Type query needs another round.
        var optimizations = reducers.Count;
        reducers.InsertRange(0, growing);
        var concurrency = SelectConcurrency(
            OperatingSystem.IsBrowser(), Environment.ProcessorCount);
        var methodAnalyzerWorkers =
            System.Collections.Immutable.ImmutableArray
                .CreateBuilder<IReachableMethodAnalyzer>(concurrency.WorkerCount + 1);
        var memo = optimizations == 0 ? null : reducerMemo;
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
        var genericConstraints = new ClosedGenericConstraintValidator(
            new MetadataGenericParameterConstraintReader(metadata, typeDefinitions),
            new GenericArgumentSpecialConstraintValidator(typeDefinitions, methodRepository, nullableTypes),
            relationships, typeDefinitions);
        var importClassifier = importClassifiers.Create(
            typeFinder,
            typeDefinitions,
            methodRepository,
            delegateTypeRecognizer,
            asyncBindingResolver,
            new JavaScriptConversionResolver(typeFinder, methodRepository, methodInstances, symbols),
            intrinsics,
            symbols,
            genericConstraints);
        var objectDispatchSharing = new ObjectDispatchSharing(optimizationSettings.ObjectDispatch);
        var programBuilder = new RequiredTypeDependencyProgramBuilder(programBuilders.Create(
            allocationCapabilities.Create(
                typeRepository,
                fieldRepository,
                methodRepository,
                intrinsics,
                typeClassifiers.Create(metadata)),
            byRefEffects.Create(methodRepository, intrinsics),
            delegateBindingPlanners.Create(relationships),
            objectDispatchSharing),
            new RequiredTypeDependencyValidator(typeDefinitions, baseTypeIdentities, implementedInterfaces, genericConstraints),
            typeIdentities, new CompilerDiagnosticDependencyPathBinder());
        var dispatchTargetResolver = dispatchTargets.Create(
            typeFinder,
            typeDefinitions,
            methodRepository,
            methodImplementations,
            implementedInterfaces,
            methodInstances,
            symbols,
            relationships,
            baseTypes);
        var closure = new ReachabilityClosureBuilder(
            new ReachabilitySessionFactory(
                ledgers,
                new GenericCycleSessionFactory(typeFinder, methodRepository, methodInstances, symbols,
                    new CalleeMethodBodyReader(methodBodies)),
                methodBatchAnalyzer,
                new DispatchCandidateIndexFactory(relationships, relationshipKeys),
                delegateTypeRecognizer,
                fieldRepository,
                new FinalizerResolver(
                    typeDefinitions,
                    methodRepository,
                    baseTypes),
                importClassifier,
                new MethodInitializationTriggerClassifier(typeRepository),
                new IntrinsicAddressBodyPlanner(intrinsics),
                methodInstances,
                methodRepository,
                new ModuleInitializerResolver(
                    metadata.Types,
                    metadata.Methods),
                nullableTypes,
                symbols,
                typeDefinitions,
                typeFinder,
                typeIdentities,
                typeRepository),
            new ReachabilityRootSeeder(methodRepository, typeDefinitions, typeIdentities, typeFinder),
            new ReachabilityQueueDrainer(
                new ReachableMethodApplier(typeIdentities),
                new ReachabilityImportApplier(typeIdentities),
                dispatchTargetResolver,
                managedCallSites,
                new CompilerDiagnosticProvenanceBinder(),
                reachabilityClosureObserver,
                reachabilityProgressObserver),
            new ReachabilityDerivedRootExpander(
                new RuntimeReachabilityExpander(
                    new SharedObjectDispatchResolver(typeFinder, methodRepository, methodInstances, symbols),
                    objectDispatchSharing,
                    delegateTypeRecognizer,
                    new RuntimeIntrinsicTypeRootPlanner(intrinsics, nullableTypes, typeDefinitions,
                        new ArrayCreationRootPlanner(typeDefinitions),
                        new StructureMarshallingClosure(typeDefinitions, fieldRepository, baseTypes)),
                    typeDefinitions,
                    typeIdentities,
                    new ObjectArrayDelegateAdapterPlanner(
                        intrinsics,
                        delegateTypeRecognizer,
                        typeFinder,
                        typeDefinitions,
                        methodRepository,
                        methodInstances,
                        symbols)),
                new MemberReachabilityExpander(
                    new MemberDescriptorPlanner(new MetadataPropertyAccessorResolver(
                        [.. metadata.Assemblies.SelectMany(assembly =>
                            assembly.Properties.Values)],
                        methodInstances)),
                    new MemberExecutionPlanner(
                        intrinsics,
                        delegateTypeRecognizer,
                        typeFinder,
                        methodRepository,
                        methodInstances,
                        symbols,
                        nullableTypes,
                        typeDefinitions),
                    dispatchTargetResolver,
                    new DispatchTargetBodyClassifier(intrinsics)),
                new TypeFactsReachabilityExpander(
                    new RuntimeTypeFactsClosurePlanner(baseTypes, typeDefinitions, typeIdentities),
                    typeIdentities)),
            programBuilder,
            delegateTypeRecognizer,
            typeTestPlanners.Create(types, relationships, relationshipKeys),
            new ModuleInitializerOrderer(BuildModuleDependencies(metadata)));
        return new WholeProgramAnalyzer(new ReducingReachabilityClosureBuilder(closure,
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
