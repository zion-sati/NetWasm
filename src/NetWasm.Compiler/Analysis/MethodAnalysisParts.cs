using System;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

/// <summary>The factories a compile's per-method analysis is built from.</summary>
internal sealed class MethodAnalysisFactories(
    ICalledMethodResolverFactory calledMethods,
    ITypeOperandResolverFactory typeOperands,
    IMethodSpecializerFactory specializers,
    IManagedMethodOptimizerFactory optimizers,
    IAttributeQueryMethodSpecializerFactory attributeQueries,
    IDispatchTargetResolverFactory dispatchTargets,
    ITypeRelationshipClassifierFactory typeRelationships,
    ITypeRelationshipKeyResolverFactory typeRelationshipKeys,
    IDelegateTypeRecognizerFactory delegateTypes,
    IReachabilityInstructionAnalyzerFactory instructionAnalyzers,
    IReachableMethodAnalyzerFactory methodAnalyzers,
    IImplicitExceptionDiscoveryFactory exceptionDiscoveries,
    IBaseTypeResolverFactory baseTypeResolvers,
    IDelegateMethodClassifierFactory delegateMethodClassifiers,
    IDispatchSiteKeyBuilder dispatchSiteKeys,
    IControlFlowGraphBuilderFactory graphBuilders)
{
    public ICalledMethodResolverFactory CalledMethods { get; } = calledMethods;
    public ITypeOperandResolverFactory TypeOperands { get; } = typeOperands;
    public IMethodSpecializerFactory Specializers { get; } = specializers;
    public IManagedMethodOptimizerFactory Optimizers { get; } = optimizers;
    public IAttributeQueryMethodSpecializerFactory AttributeQueries { get; } = attributeQueries;
    public IDispatchTargetResolverFactory DispatchTargets { get; } = dispatchTargets;
    public ITypeRelationshipClassifierFactory TypeRelationships { get; } = typeRelationships;
    public ITypeRelationshipKeyResolverFactory TypeRelationshipKeys { get; } = typeRelationshipKeys;
    public IDelegateTypeRecognizerFactory DelegateTypes { get; } = delegateTypes;
    public IReachabilityInstructionAnalyzerFactory InstructionAnalyzers { get; } = instructionAnalyzers;
    public IReachableMethodAnalyzerFactory MethodAnalyzers { get; } = methodAnalyzers;
    public IImplicitExceptionDiscoveryFactory ExceptionDiscoveries { get; } = exceptionDiscoveries;
    public IBaseTypeResolverFactory BaseTypeResolvers { get; } = baseTypeResolvers;
    public IDelegateMethodClassifierFactory DelegateMethodClassifiers { get; } = delegateMethodClassifiers;
    public IDispatchSiteKeyBuilder DispatchSiteKeys { get; } = dispatchSiteKeys;
    public IControlFlowGraphBuilderFactory GraphBuilders { get; } = graphBuilders;
}

/// <summary>
/// What a compile's per-method analysis consults, built once from its metadata, settings
/// and platform. The whole-program analyzer and a standalone method analyzer are built from
/// the same parts, so a method analysed alone is analysed as the closure analyses it.
/// </summary>
internal sealed class MethodAnalysisParts
{
    private MethodAnalysisFactories _factories = null!;
    private IMethodBodyReader _methodBodies = null!;
    private ITypeRepository _typeRepository = null!;
    private IFieldRepository _fieldRepository = null!;
    private IMethodRepository _methodRepository = null!;
    private ITypeDefinitionResolver _typeDefinitions = null!;
    private ISymbolFormatter _symbols = null!;
    private IRuntimeIntrinsicRegistry _intrinsics = null!;
    private WasmOptimizationSettings _settings = null!;
    private ConstantOperandReader _operands = null!;

    public IBaseTypeResolver BaseTypes { get; private init; } = null!;
    public ITypeRelationshipClassifier Relationships { get; private init; } = null!;
    public ITypeRelationshipKeyResolver RelationshipKeys { get; private init; } = null!;
    public IDelegateTypeRecognizer DelegateTypeRecognizer { get; private init; } = null!;
    public ICalledMethodResolver Calls { get; private init; } = null!;
    public ITypeOperandResolver Types { get; private init; } = null!;
    public IReachabilityInstructionAnalyzer InstructionAnalyzer { get; private init; } = null!;
    public IMethodSpecializer Specializer { get; private init; } = null!;
    public IMethodRewriteRule? ConstantCallFolding { get; private set; }
    public IManagedMethodOptimizer Optimizer { get; private set; } = null!;
    public IDispatchTargetResolver DispatchTargets { get; private set; } = null!;

    internal static MethodAnalysisParts Build(
        MethodAnalysisFactories factories,
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
        IRuntimeIntrinsicRegistry intrinsics,
        WasmOptimizationSettings optimizationSettings,
        PlatformWorldCapabilities world)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var baseTypes = factories.BaseTypeResolvers.Create(
            typeFinder,
            typeIdentities,
            baseTypeIdentities);
        var relationships = factories.TypeRelationships.Create(
            typeFinder,
            typeDefinitions,
            typeIdentities,
            implementedInterfaces,
            baseTypes);
        var relationshipKeys = factories.TypeRelationshipKeys.Create(
            typeFinder,
            typeDefinitions,
            typeIdentities,
            implementedInterfaces,
            baseTypes);
        var delegateTypeRecognizer = factories.DelegateTypes.Create(typeDefinitions, baseTypes);
        var delegateMethodClassifier = factories.DelegateMethodClassifiers.Create(
            delegateTypeRecognizer);
        var calls = factories.CalledMethods.Create(methodRepository, methodInstances, symbols);
        var types = factories.TypeOperands.Create(typeIdentities);
        var instructionAnalyzer = factories.InstructionAnalyzers.Create(
            typeFinder,
            typeIdentities,
            calls,
            types,
            factories.DispatchSiteKeys,
            delegateMethodClassifier);
        var specializer = factories.Specializers.Create(
            typeDefinitions,
            typeRepository,
            methodRepository,
            methodInstances,
            symbols,
            fieldRepository,
            typeFinder,
            calls,
            methodImplementations,
            relationships,
            methodBodies,
            optimizationSettings,
            intrinsics,
            world,
            typeIdentities);
        specializer = factories.AttributeQueries.Create(specializer, metadata, typeRepository, fieldRepository,
            methodRepository, typeFinder, typeDefinitions, typeIdentities, methodInstances,
            relationships, baseTypes, calls, types, symbols);
        var parts = new MethodAnalysisParts
        {
            _factories = factories,
            _methodBodies = methodBodies,
            _typeRepository = typeRepository,
            _fieldRepository = fieldRepository,
            _methodRepository = methodRepository,
            _typeDefinitions = typeDefinitions,
            _symbols = symbols,
            _intrinsics = intrinsics,
            _settings = optimizationSettings,
            _operands = new ConstantOperandReader(calls, fieldRepository, typeRepository, methodRepository),
            BaseTypes = baseTypes,
            Relationships = relationships,
            RelationshipKeys = relationshipKeys,
            DelegateTypeRecognizer = delegateTypeRecognizer,
            Calls = calls,
            Types = types,
            InstructionAnalyzer = instructionAnalyzer,
            Specializer = specializer,
        };
        parts.ConstantCallFolding = optimizationSettings.ConstantCallFolding == ConstantCallFoldingPolicy.Conservative
            ? new ConstantCallFoldingRule(parts.ConstantCalls(), new MethodSpecializer(
            [
                new KnownSizeRewriter(calls),
                ConstantBranchFoldingFactory.Create(factories.GraphBuilders.Create()),
            ]))
            : null;
        parts.DispatchTargets = factories.DispatchTargets.Create(
            typeFinder,
            typeDefinitions,
            methodRepository,
            methodImplementations,
            implementedInterfaces,
            methodInstances,
            symbols,
            relationships,
            baseTypes);
        parts.Optimizer = factories.Optimizers.Create(
            methodBodies,
            typeRepository,
            fieldRepository,
            methodRepository,
            specializer,
            calls,
            intrinsics,
            // Folding before inlining sees constant arguments before they are spilled to locals.
            new MethodSpecializer(parts.ConstantCallFolding is null ? [] : [parts.ConstantCallFolding]),
            parts.DispatchTargets,
            new FinalizerResolver(typeDefinitions, methodRepository, baseTypes),
            typeDefinitions,
            new Loops.ArrayLoopMethodResolver(typeFinder, methodRepository, methodInstances, symbols),
            new Loops.SpanLengthResolver(typeFinder, methodRepository, methodInstances, symbols),
            new Loops.VectorLoopMethodResolver(typeFinder, methodRepository, methodInstances, symbols),
            optimizationSettings);
        return parts;
    }

    internal IMethodRewriteRule ConstantCalls() => new ConstantCallRewriter(_factories.GraphBuilders.Create(), Calls,
        new ConstantCallEvaluator(_methodBodies, Calls, _typeDefinitions, _methodRepository, _intrinsics, _operands,
            new ConstantComparisonEvaluator(_typeDefinitions), Specializer),
        _operands);

    // One method analyzer; each worker of the closure gets its own.
    internal IReachableMethodAnalyzer CreateAnalyzer() => _factories.MethodAnalyzers.Create(
        _methodBodies,
        _typeRepository,
        _fieldRepository,
        _methodRepository,
        Specializer,
        Optimizer,
        Calls,
        _settings.IntegerCheckElimination,
        _factories.ExceptionDiscoveries.Create(_methodRepository, _symbols, _intrinsics),
        InstructionAnalyzer);
}

/// <summary>A method analyzer, and a planner of the sites of what it analysed.</summary>
internal sealed class StandaloneMethodAnalysis(IReachableMethodAnalyzer analyzer, IAnalysedSitePlanner sites)
{
    public IReachableMethodAnalyzer Analyzer { get; } = analyzer;
    public IAnalysedSitePlanner Sites { get; } = sites;
}

internal interface IStandaloneMethodAnalyzerFactory
{
    // A method analyzer built as the whole-program analyzer builds its own, for analysing
    // single methods without the closure.
    StandaloneMethodAnalysis Create(
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
        IRuntimeIntrinsicRegistry intrinsics,
        WasmOptimizationSettings optimizationSettings,
        PlatformWorldCapabilities world);
}

internal sealed class StandaloneMethodAnalyzerFactory(
    ICalledMethodResolverFactory calledMethods,
    ITypeOperandResolverFactory typeOperands,
    IMethodSpecializerFactory specializers,
    IManagedMethodOptimizerFactory optimizers,
    IAttributeQueryMethodSpecializerFactory attributeQueries,
    IDispatchTargetResolverFactory dispatchTargets,
    ITypeRelationshipClassifierFactory typeRelationships,
    ITypeRelationshipKeyResolverFactory typeRelationshipKeys,
    IDelegateTypeRecognizerFactory delegateTypes,
    IReachabilityInstructionAnalyzerFactory instructionAnalyzers,
    IReachableMethodAnalyzerFactory methodAnalyzers,
    IImplicitExceptionDiscoveryFactory exceptionDiscoveries,
    IBaseTypeResolverFactory baseTypeResolvers,
    IDelegateMethodClassifierFactory delegateMethodClassifiers,
    IDispatchSiteKeyBuilder dispatchSiteKeys,
    IControlFlowGraphBuilderFactory graphBuilders,
    ITypeTestPlannerFactory typeTestPlanners) : IStandaloneMethodAnalyzerFactory
{
    public StandaloneMethodAnalysis Create(
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
        IRuntimeIntrinsicRegistry intrinsics,
        WasmOptimizationSettings optimizationSettings,
        PlatformWorldCapabilities world)
    {
        var parts = MethodAnalysisParts.Build(
            new(calledMethods, typeOperands, specializers, optimizers, attributeQueries, dispatchTargets,
                typeRelationships, typeRelationshipKeys, delegateTypes, instructionAnalyzers, methodAnalyzers,
                exceptionDiscoveries, baseTypeResolvers, delegateMethodClassifiers, dispatchSiteKeys, graphBuilders),
            metadata, typeRepository, fieldRepository, methodRepository, typeFinder, typeDefinitions, typeIdentities,
            baseTypeIdentities, implementedInterfaces, methodBodies, methodInstances, methodImplementations, symbols,
            intrinsics, optimizationSettings, world);
        return new(parts.CreateAnalyzer(), new AnalysedSitePlanner(
            new DispatchCandidateIndexFactory(parts.Relationships, parts.RelationshipKeys), parts.DispatchTargets,
            typeTestPlanners.Create(parts.Types, parts.Relationships, parts.RelationshipKeys)));
    }
}
