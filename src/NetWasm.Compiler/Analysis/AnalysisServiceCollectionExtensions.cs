using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis;

internal static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerAnalysis(this IServiceCollection services)
    {
        services.AddManagedCallSites();
        services.AddManagedCallSiteAnalysis();
        services.AddDelegateAnalysis();
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<INullableTypeResolver, NullableTypeResolver>();
        services.AddSingleton<ICalledMethodResolverFactory,
            CalledMethodResolverFactory>();
        services.AddSingleton<IBaseTypeResolverFactory,
            BaseTypeResolverFactory>();
        services.AddSingleton<ITypeOperandResolverFactory,
            TypeOperandResolverFactory>();
        services.AddSingleton<IMethodSpecializerFactory, MethodSpecializerFactory>();
        services.AddSingleton<IDispatchTargetResolverFactory,
            DispatchTargetResolverFactory>();
        services.AddSingleton<ITypeRelationshipClassifierFactory,
            TypeRelationshipClassifierFactory>();
        services.AddSingleton<IDispatchSiteKeyBuilder, DispatchSiteKeyBuilder>();
        services.AddSingleton<IDelegateTypeRecognizerFactory,
            DelegateTypeRecognizerFactory>();
        services.AddSingleton<IDelegateMethodClassifierFactory,
            DelegateMethodClassifierFactory>();
        services.AddSingleton<IReachabilityInstructionAnalyzerFactory,
            ReachabilityInstructionAnalyzerFactory>();
        services.AddSingleton<IReachabilityImportClassifierFactory,
            ReachabilityImportClassifierFactory>();
        services.AddSingleton<IReachableMethodAnalyzerFactory,
            ReachableMethodAnalyzerFactory>();
        services.AddSingleton<ITypeTestPlannerFactory, TypeTestPlannerFactory>();
        services.AddSingleton<IRuntimeIntrinsicRegistryFactory,
            RuntimeIntrinsicRegistryFactory>();
        services.AddSingleton<IImplicitExceptionDiscoveryFactory,
            ImplicitExceptionDiscoveryFactory>();
        services.AddSingleton<IStringConstructionExceptionRequirementProvider,
            StringConstructionExceptionRequirementProvider>();
        services.AddSingleton<IReachableProgramBuilderFactory,
            ReachableProgramBuilderFactory>();
        services.AddSingleton<IAllocationCapabilityAnalyzerFactory,
            AllocationCapabilityAnalyzerFactory>();
        services.AddSingleton<IReachabilityLedgerFactory,
            ReachabilityLedgerFactory>();
        services.AddSingleton<IReachableMethodBatchObserver, LoggingReachableMethodBatchObserver>();
        services.AddSingleton<IReachabilityClosureObserver, LoggingReachabilityClosureObserver>();
        services.AddSingleton<IWholeProgramAnalyzerFactory, WholeProgramAnalyzerFactory>();
        return services;
    }
}
