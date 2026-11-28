// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Planning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class MethodEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmMethodEmission(
        this IServiceCollection services)
    {
        services.AddAddressTakenValuePromotion();
        services.AddManagedValuePlanning();
        services.AddArrayMetadataCaching();
        services.AddScalarCallPreservation();
        services.AddStructResults();
        services.AddSingleton<IManagedDefinitionFunctionAppender,
            ManagedDefinitionFunctionAppender>();
        services.AddSingleton<IManagedDefinitionSetAppender,
            ManagedDefinitionSetAppender>();
        services.AddSingleton<IConstructedMethodFunctionAppender,
            ConstructedMethodFunctionAppender>();
        services.AddSingleton<IConstructedMethodSetAppender,
            ConstructedMethodSetAppender>();
        services.AddSingleton<IManagedMethodEmissionMetricProjector,
            ManagedMethodEmissionMetricProjector>();
        services.AddSingleton<IManagedHeapSampler, ManagedHeapSampler>();
        services.AddSingleton<IExceptionGroupEnumerator, ExceptionGroupEnumerator>();
        services.AddSingleton<IFilterEmissionCountMerger,
            FilterEmissionCountMerger>();
        services.AddSingleton<IManagedMethodFunctionTypeResolver,
            ManagedMethodFunctionTypeResolver>();
        services.AddSingleton<IDirectValueLoadSelector,
            DirectValueLoadSelector>();
        services.AddSingleton<ITransientAggregatePromotionPlanner,
            TransientAggregatePromotionPlanner>();
        services.AddSingleton<IValueFrameSlotAlignmentResolver, SimdValueFrameSlotAlignmentResolver>();
        services.AddSingleton<IValueFrameLayoutPlanner, ValueFrameLayoutPlanner>();
        services.AddSingleton<IValueFrameReferenceMapPlanner, ValueFrameReferenceMapPlanner>();
        services.AddSingleton<FilterEnvironmentLayoutPlanner>();
        services.AddSingleton<IFilterEnvironmentLayoutPlanner>(provider =>
            provider.GetRequiredService<FilterEnvironmentLayoutPlanner>());
        services.AddSingleton<ValueFrameAddressEmitter>();
        services.AddSingleton<IValueFrameAddressEmitter>(provider =>
            provider.GetRequiredService<ValueFrameAddressEmitter>());
        services.AddSingleton<IValueFrameInitializationEmitter,
            ValueFrameInitializationEmitter>();
        services.AddSingleton<IFilterEnvironmentRootEmitter,
            FilterEnvironmentRootEmitter>();
        services.AddSingleton<IStackTraceFrameEntryEmitter,
            StackTraceFrameEntryEmitter>();
        services.AddSingleton<IStackTraceFrameLocationEmitter,
            StackTraceFrameLocationEmitter>();
        services.AddSingleton<IStackTraceFrameExitEmitter,
            StackTraceFrameExitEmitter>();
        services.AddSingleton<IMethodFrameEntryEmitter, MethodFrameEntryEmitter>();
        services.AddSingleton<IValueFrameEntryEmitter, ValueFrameEntryEmitter>();
        services.AddSingleton<ITerminalBranchValueFramePlanner, TerminalBranchValueFramePlanner>();
        services.AddSingleton<ITerminalBranchValueFrameEmitter, TerminalBranchValueFrameEmitter>();
        services.AddSingleton<IMethodFrameExitEmitter, MethodFrameExitEmitter>();
        services.AddSingleton<ManagedMethodEmitter>();
        services.AddSingleton<IAuthoritativeRootSourceSelector, AuthoritativeRootSourceSelector>();
        services.AddSingleton<IAuthoritativeRootLayoutPlanner, AuthoritativeRootLayoutPlanner>();
        services.AddSingleton<IStructuredBlockPredecessorReader, StructuredBlockPredecessorReader>();
        services.AddSingleton<IRootClearElisionPlanner, RootClearElisionPlanner>();
        services.AddSingleton<IRootFrameEntryClearPlanner, RootFrameEntryClearPlanner>();
        services.AddSingleton<IFallbackPublicationClassifier, FallbackPublicationClassifier>();
        services.AddSingleton<ILazyRootFramePlanner, LazyRootFramePlanner>();
        services.AddSingleton<ILazyRootFrameEmitter, LazyRootFrameEmitter>();
        services.AddSingleton<IRootValueElisionPlanner, RootValueElisionPlanner>();
        services.AddSingleton<IInstructionCountingWriterFactory,
            InstructionCountingWriterFactory>();
        services.AddSingleton<IManagedMethodEmitter>(provider =>
            provider.GetRequiredService<ManagedMethodEmitter>());
        services.AddSingleton<IPassThroughCatchLocalFinder, PassThroughCatchLocalFinder>();
        services.AddSingleton<ILandingPadRestoreEmitter, LandingPadRestoreEmitter>();
        services.AddSingleton<ILandingPadRestorePlanner, LandingPadRestorePlanner>();
        services.AddSingleton<IExceptionFrameCallEmitter, ExceptionFrameCallEmitter>();
        services.AddSingleton<MarkedExceptionRegion>();
        services.AddSingleton<RegisteredExceptionFrame>();
        services.AddSingleton(provider => new PassThroughExceptionFrame(
            provider.GetRequiredService<NetWasm.Compiler.Core.ITargetLayout>(),
            provider.GetRequiredService<IExceptionFrameCallEmitter>(),
            provider.GetRequiredService<IPassThroughCatchLocalFinder>(),
            provider.GetRequiredService<ILandingPadRestoreEmitter>(),
            provider.GetRequiredService<ILazyRootFrameEmitter>()));
        services.AddSingleton(provider => new FilterGuardedCatchFrame(
            provider.GetRequiredService<NetWasm.Compiler.Core.ITargetLayout>(),
            provider.GetRequiredService<IExceptionFrameCallEmitter>(),
            provider.GetRequiredService<ILandingPadRestoreEmitter>(),
            provider.GetRequiredService<ILazyRootFrameEmitter>(),
            provider.GetRequiredService<RegisteredExceptionFrame>()));
        services.AddSingleton(provider => new UnregisteredCatchFrame(
            provider.GetRequiredService<IExceptionFrameCallEmitter>(),
            provider.GetRequiredService<ILandingPadRestoreEmitter>(),
            provider.GetRequiredService<ILazyRootFrameEmitter>()));
        services.AddSingleton<IExceptionFrameStrategySelector>(provider => new ExceptionFrameStrategySelector(
            provider.GetRequiredService<MarkedExceptionRegion>(),
            provider.GetRequiredService<PassThroughExceptionFrame>(),
            new PassThroughExceptionFrame(
                provider.GetRequiredService<NetWasm.Compiler.Core.ITargetLayout>(),
                provider.GetRequiredService<IExceptionFrameCallEmitter>(),
                provider.GetRequiredService<IPassThroughCatchLocalFinder>(),
                provider.GetRequiredService<ILandingPadRestoreEmitter>(),
                provider.GetRequiredService<ILazyRootFrameEmitter>(),
                registersUnderFilters: false),
            provider.GetRequiredService<FilterGuardedCatchFrame>(),
            provider.GetRequiredService<UnregisteredCatchFrame>(),
            provider.GetRequiredService<RegisteredExceptionFrame>()));
        services.AddSingleton<IExceptionFrameStepEmitter, ExceptionFrameStepEmitter>();
        services.AddSingleton<ExceptionRegionEmitter>();
        services.AddSingleton<IExceptionRegionEmitter>(provider =>
            provider.GetRequiredService<ExceptionRegionEmitter>());
        services.AddSingleton<IDispatcherBranchTablePlanner,
            DispatcherBranchTablePlanner>();
        services.AddSingleton<IControlFlowDispatcherEmitter,
            ControlFlowDispatcherEmitter>();
        services.AddSingleton<StructuredControlFlowEmitter>();
        services.AddSingleton<IStructuredConditionValueEmitter, StructuredConditionValueEmitter>();
        services.AddSingleton<IStructuredControlFlowEmitter>(provider =>
            provider.GetRequiredService<StructuredControlFlowEmitter>());
        services.AddSingleton<IStructuredLeaveEmitter, StructuredLeaveEmitter>();
        services.AddSingleton<IBranchComparisonEmitter, BranchComparisonEmitter>();
        services.AddSingleton<IManagedMethodSequenceEmitter,
            ManagedMethodSequenceEmitter>();
        services.TryAddSingleton<IMethodDefectExceptionFactory, MethodDefectExceptionFactory>();
        services.AddSingleton<IManagedMethodBodyEmitterFactory,
            ManagedMethodBodyEmitterFactory>();
        // Deferred methods are structured with the compile's structuring, which a compile
        // that defers registers.
        services.AddSingleton<IManagedMethodBodyEmitter>(provider => new OutlineStructuringBodyEmitter(
            provider.GetRequiredService<IManagedMethodBodyEmitterFactory>().Create(),
            provider.GetService<IWasmMethodLowerer>()));
        return services;
    }
}
