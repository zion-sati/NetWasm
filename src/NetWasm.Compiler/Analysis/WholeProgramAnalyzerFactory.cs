using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis;

internal sealed class WholeProgramAnalyzerFactory(
    ICalledMethodResolverFactory calledMethods,
    ITypeOperandResolverFactory typeOperands,
    IMethodSpecializerFactory specializers,
    IDispatchTargetResolverFactory dispatchTargets,
    ITypeRelationshipClassifierFactory typeRelationships,
    IDelegateTypeRecognizerFactory delegateTypes,
    IReachabilityImportClassifierFactory importClassifiers,
    IReachabilityInstructionAnalyzerFactory instructionAnalyzers,
    IReachableMethodAnalyzerFactory methodAnalyzers,
    ITypeTestPlannerFactory typeTestPlanners,
    IImplicitExceptionDiscoveryFactory exceptionDiscoveries,
    IReachableProgramBuilderFactory programBuilders,
    IReachabilityLedgerFactory ledgers,
    IAllocationCapabilityAnalyzerFactory allocationCapabilities,
    IBaseTypeResolverFactory baseTypeResolvers,
    IDelegateMethodClassifierFactory delegateMethodClassifiers,
    IDispatchSiteKeyBuilder dispatchSiteKeys,
    IDelegateBindingPlannerFactory delegateBindingPlanners,
    IManagedCallSiteLedgerWriter managedCallSites,
    INullableTypeResolver nullableTypes,
    IReachableMethodBatchObserver reachableMethodBatchObserver,
    IReachabilityClosureObserver reachabilityClosureObserver) : IWholeProgramAnalyzerFactory
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
        IRuntimeIntrinsicRegistry intrinsics)
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
        var baseTypes = baseTypeResolvers.Create(
            typeFinder,
            typeIdentities,
            baseTypeIdentities);
        var relationships = typeRelationships.Create(
            typeFinder,
            typeDefinitions,
            typeIdentities,
            implementedInterfaces,
            baseTypes);
        var delegateTypeRecognizer = delegateTypes.Create(typeDefinitions, baseTypes);
        var delegateMethodClassifier = delegateMethodClassifiers.Create(
            delegateTypeRecognizer);
        var calls = calledMethods.Create(methodRepository, methodInstances, symbols);
        var types = typeOperands.Create(typeIdentities);
        var instructionAnalyzer = instructionAnalyzers.Create(
            typeFinder,
            typeIdentities,
            calls,
            types,
            dispatchSiteKeys,
            delegateMethodClassifier);
        var specializer = specializers.Create(
            typeDefinitions,
            methodRepository,
            methodInstances,
            symbols,
            fieldRepository,
            typeFinder,
            calls,
            methodImplementations);
        var browserHost = OperatingSystem.IsBrowser();
        var workerCount = browserHost ? 1 : Environment.ProcessorCount;
        var methodAnalyzerWorkers =
            System.Collections.Immutable.ImmutableArray
                .CreateBuilder<IReachableMethodAnalyzer>(workerCount);
        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            methodAnalyzerWorkers.Add(methodAnalyzers.Create(
            methodBodies,
            typeRepository,
            fieldRepository,
            methodRepository,
            specializer,
            exceptionDiscoveries.Create(methodRepository, symbols, intrinsics),
            instructionAnalyzer));
        }
        var methodAnalyzerBatch = methodAnalyzerWorkers.MoveToImmutable();
        IReachableMethodBatchAnalyzer methodBatchAnalyzer = browserHost
            ? new SynchronousReachableMethodBatchAnalyzer(
                methodAnalyzerBatch[0],
                reachableMethodBatchObserver)
            : new ReachableMethodBatchAnalyzer(
                methodAnalyzerBatch,
                reachableMethodBatchObserver);
        var asyncBindingResolver = new DelegateJavaScriptAsyncBindingResolver(
            javaScriptAsyncBindings);
        var importClassifier = importClassifiers.Create(
            typeFinder,
            typeDefinitions,
            methodRepository,
            delegateTypeRecognizer,
            asyncBindingResolver,
            intrinsics,
            symbols);
        var programBuilder = programBuilders.Create(
            allocationCapabilities.Create(
                typeRepository,
                fieldRepository,
                methodRepository),
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
            typeTestPlanners.Create(types, relationships),
            programBuilder,
            ledgers,
            managedCallSites,
            new RuntimeIntrinsicTypeRootPlanner(intrinsics, nullableTypes),
            reachabilityClosureObserver,
            new DispatchCandidateIndexFactory(relationships),
            new ModuleInitializerResolver(
                metadata.Types,
                metadata.Methods),
            new ModuleInitializerOrderer(BuildModuleDependencies(metadata)));
        return new WholeProgramAnalyzer(closure);
    }

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

internal sealed class DelegateJavaScriptAsyncBindingResolver(
    Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> resolve) :
    IJavaScriptAsyncBindingResolver
{
    private readonly Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> _resolve =
        resolve ?? throw new ArgumentNullException(nameof(resolve));

    public JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method) =>
        _resolve(method);
}
