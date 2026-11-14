using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class ReachabilitySessionFactory(
    IReachabilityLedgerFactory ledgers,
    IGenericCycleSessionFactory genericCycles,
    IReachableMethodAnalysisSessionFactory methodAnalysis,
    IDispatchCandidateIndexFactory dispatchCandidateIndexes,
    IDelegateTypeRecognizer delegateTypes,
    IFieldRepository fieldRepository,
    IFinalizerResolver finalizerResolver,
    IReachabilityImportClassifier importClassifier,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IIntrinsicAddressBodyPlanner intrinsicAddressBodies,
    IMethodInstanceResolver methodInstances,
    IMethodRepository methodRepository,
    IModuleInitializerResolver moduleInitializers,
    INullableTypeResolver nullableTypes,
    ISymbolFormatter symbols,
    ITypeDefinitionResolver typeDefinitions,
    ITypeFinder typeFinder,
    ITypeIdentityResolver typeIdentities,
    ITypeRepository typeRepository) : IReachabilitySessionFactory
{
    private readonly IReachabilityLedgerFactory _ledgers = ledgers ??
        throw new ArgumentNullException(nameof(ledgers));
    private readonly IGenericCycleSessionFactory _genericCycles = genericCycles ??
        throw new ArgumentNullException(nameof(genericCycles));
    private readonly IReachableMethodAnalysisSessionFactory _methodAnalysis = methodAnalysis ??
        throw new ArgumentNullException(nameof(methodAnalysis));
    private readonly IDispatchCandidateIndexFactory _dispatchCandidateIndexes = dispatchCandidateIndexes ??
        throw new ArgumentNullException(nameof(dispatchCandidateIndexes));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));
    private readonly IFieldRepository _fieldRepository = fieldRepository ??
        throw new ArgumentNullException(nameof(fieldRepository));
    private readonly IFinalizerResolver _finalizerResolver = finalizerResolver ??
        throw new ArgumentNullException(nameof(finalizerResolver));
    private readonly IReachabilityImportClassifier _importClassifier = importClassifier ??
        throw new ArgumentNullException(nameof(importClassifier));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly IIntrinsicAddressBodyPlanner _intrinsicAddressBodies = intrinsicAddressBodies ??
        throw new ArgumentNullException(nameof(intrinsicAddressBodies));
    private readonly IMethodInstanceResolver _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly IMethodRepository _methodRepository = methodRepository ??
        throw new ArgumentNullException(nameof(methodRepository));
    private readonly IModuleInitializerResolver _moduleInitializers = moduleInitializers ??
        throw new ArgumentNullException(nameof(moduleInitializers));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ISymbolFormatter _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));
    private readonly ITypeRepository _typeRepository = typeRepository ??
        throw new ArgumentNullException(nameof(typeRepository));

    public ReachabilitySession Create(
        MethodDefinitionModel? entryPoint,
        IReadOnlySet<EntityKey> outwardBoundaries,
        ImmutableDictionary<string, CilMethodBody>? rewrittenBodies)
    {
        ArgumentNullException.ThrowIfNull(outwardBoundaries);
        var state = _ledgers.Create();
        var genericCycles = _genericCycles.Create();
        var methodAnalysis = _methodAnalysis.Begin();
        return new ReachabilitySession(
            state,
            methodAnalysis,
            genericCycles,
            _dispatchCandidateIndexes.Create(),
            outwardBoundaries,
            entryPoint,
            rewrittenBodies,
            _delegateTypes,
            _fieldRepository,
            _finalizerResolver,
            _importClassifier,
            _initializationTriggers,
            _intrinsicAddressBodies,
            _methodInstances,
            _methodRepository,
            _moduleInitializers,
            _nullableTypes,
            _symbols,
            _typeDefinitions,
            _typeFinder,
            _typeIdentities,
            _typeRepository);
    }
}
