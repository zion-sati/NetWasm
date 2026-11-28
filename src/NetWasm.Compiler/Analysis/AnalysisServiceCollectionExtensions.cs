// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Analysis;

internal static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerAnalysis(this IServiceCollection services)
    {
        services.AddManagedCallSites();
        services.AddManagedCallSiteAnalysis();
        services.AddDelegateAnalysis();
        services.AddAttributeAnalysis();
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<INullableTypeResolver, NullableTypeResolver>();
        services.AddSingleton<ICalledMethodResolverFactory,
            CalledMethodResolverFactory>();
        services.AddSingleton<PlatformServices.IPlatformServiceBindingResolverFactory,
            PlatformServices.PlatformServiceBindingResolverFactory>();
        services.AddSingleton<Metadata.IAssemblyAttributeDescriptorReaderFactory, Metadata.AssemblyAttributeDescriptorReaderFactory>();
        services.AddSingleton<IBaseTypeResolverFactory,
            BaseTypeResolverFactory>();
        services.AddSingleton<ITypeOperandResolverFactory,
            TypeOperandResolverFactory>();
        services.AddSingleton<IMethodSpecializerFactory, MethodSpecializerFactory>();
        services.AddSingleton<Arrays.IArrayBoundsCheckEliminatorFactory, Arrays.ArrayBoundsCheckEliminatorFactory>();
        services.AddSingleton<Arrays.IRepeatedArrayAccessEliminatorFactory, Arrays.RepeatedArrayAccessEliminatorFactory>();
        services.AddSingleton<NullChecks.IConstructedObjectNullCheckEliminatorFactory,
            NullChecks.ConstructedObjectNullCheckEliminatorFactory>();
        services.AddSingleton<NullChecks.IByRefNullCheckEliminatorFactory, NullChecks.ByRefNullCheckEliminatorFactory>();
        services.AddSingleton<IIntegerCheckEliminatorFactory, IntegerCheckEliminatorFactory>();
        services.AddSingleton<IManagedMethodOptimizerFactory,
            ManagedMethodOptimizerFactory>();
        services.AddSingleton<NullChecks.IReceiverProverFactory, NullChecks.ReceiverProverFactory>();
        services.AddSingleton<Generations.IYoungConstructorProverFactory, Generations.YoungConstructorProverFactory>();
        services.AddSingleton<IDispatchTargetResolverFactory,
            DispatchTargetResolverFactory>();
        services.AddSingleton<ITypeRelationshipClassifierFactory,
            TypeRelationshipClassifierFactory>();
        services.AddSingleton<IUnrememberedTypeRelationshipClassifierFactory,
            UnrememberedTypeRelationshipClassifierFactory>();
        services.AddSingleton<ITypeRelationshipKeyResolverFactory,
            TypeRelationshipKeyResolverFactory>();
        services.AddSingleton<IDispatchSiteKeyBuilder, DispatchSiteKeyBuilder>();
        services.AddSingleton<IDelegateTypeRecognizerFactory,
            DelegateTypeRecognizerFactory>();
        services.AddSingleton<IDelegateMethodClassifierFactory,
            DelegateMethodClassifierFactory>();
        services.AddSingleton<IReachabilityInstructionAnalyzerFactory,
            ReachabilityInstructionAnalyzerFactory>();
        services.AddSingleton<IReachabilityImportClassifierFactory,
            ReachabilityImportClassifierFactory>();
        services.AddSingleton<IEnumMetadataRequirementClassifier,
            EnumMetadataRequirementClassifier>();
        services.TryAddSingleton<INativeDeclarationValidator, NativeDeclarationValidator>();
        services.TryAddSingleton<INativeCallbackDeclarationValidator,
            NativeCallbackDeclarationValidator>();
        services.AddSingleton<ReachableMethodAnalyzerFactory>();
        services.AddSingleton<IReachableMethodAnalyzerFactory>(static provider =>
            provider.GetRequiredService<ReachableMethodAnalyzerFactory>());
        services.AddSingleton<ITypeTestPlannerFactory, TypeTestPlannerFactory>();
        services.AddSingleton<IRuntimeIntrinsicRegistryFactory,
            RuntimeIntrinsicRegistryFactory>();
        services.AddSingleton<IImplicitExceptionDiscoveryFactory,
            ImplicitExceptionDiscoveryFactory>();
        services.AddSingleton<IReachableProgramBuilderFactory,
            ReachableProgramBuilderFactory>();
        services.AddSingleton<IAllocationCapabilityAnalyzerFactory,
            AllocationCapabilityAnalyzerFactory>();
        services.AddSingleton<IManagedByRefEffectAnalyzerFactory,
            ManagedByRefEffectAnalyzerFactory>();
        services.AddSingleton<IReachabilityLedgerFactory,
            ReachabilityLedgerFactory>();
        services.AddSingleton<IReachableMethodBatchObserver, LoggingReachableMethodBatchObserver>();
        services.AddSingleton<IReachabilityClosureObserver, LoggingReachabilityClosureObserver>();
        services.AddSingleton<IReachabilityProgressObserver, ReachabilityProgressReporter>();
        services.AddSingleton<IAnalysisStepRecorder, LoggingAnalysisStepRecorder>();
        services.AddSingleton<IWholeProgramAnalyzerFactory, WholeProgramAnalyzerFactory>();
        services.AddSingleton<IStandaloneMethodAnalyzerFactory, StandaloneMethodAnalyzerFactory>();
        return services;
    }
}
