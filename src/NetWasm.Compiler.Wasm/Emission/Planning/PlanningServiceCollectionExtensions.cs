using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal static class PlanningServiceCollectionExtensions
{
    public static IServiceCollection AddWasmPlanning(this IServiceCollection services)
    {
        services.TryAddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton<IEntryPointValidator, EntryPointValidator>();
        services.TryAddSingleton<INativeDeclarationValidator, NativeDeclarationValidator>();
        services.TryAddSingleton<INativeCallbackDeclarationValidator,
            NativeCallbackDeclarationValidator>();
        services.TryAddSingleton<INativeAggregateAbiPlanner, NativeAggregateAbiPlanner>();
        services.TryAddSingleton<INativeAbiSignaturePlanner, NativeAbiSignaturePlanner>();
        services.TryAddSingleton<INativeAbiPlanner, NativeAbiPlanner>();
        services.AddSingleton<INativeImportPlanner, NativeImportPlanner>();
        services.AddSingleton<INativeCallbackPlanBuilder, NativeCallbackPlanBuilder>();
        services.AddSingleton<InteropImportPlanner>();
        services.AddSingleton<IInteropImportPlanner>(provider =>
            provider.GetRequiredService<InteropImportPlanner>());
        services.AddSingleton<IWasmModulePlanInvariantValidator,
            WasmModulePlanInvariantValidator>();
        services.AddSingleton<WasmModulePlanner>();
        services.AddSingleton<IWasmModulePlanner>(provider =>
            provider.GetRequiredService<WasmModulePlanner>());
        services.AddSingleton<IManagedBoundaryFailureDispositionResolver,
            ManagedBoundaryFailurePolicy>();
        services.AddSingleton<IManagedBoundaryPlanValidator, ManagedBoundaryPlanValidator>();
        services.AddTransient<ManagedBoundaryPlanBuilder>();
        services.AddTransient<IManagedBoundaryPlanBuilder>(provider =>
            provider.GetRequiredService<ManagedBoundaryPlanBuilder>());
        services.AddSingleton<ModuleDataPlanner>();
        services.AddSingleton<IStaticInitializerFunctionPlanner, StaticInitializerFunctionPlanner>();
        services.AddSingleton<IStructuredExceptionGroupKeyFactory, StructuredExceptionGroupKeyFactory>();
        services.AddSingleton<IStackTraceMethodPlanBuilder,
            StackTraceMethodPlanBuilder>();
        services.AddSingleton<IStackTraceMethodIdProvider,
            StackTraceMethodIdProvider>();
        services.AddSingleton<IStructuredMethodEmissionPlanner, StructuredMethodEmissionPlanner>();
        services.AddSingleton<IModuleDataPlanner>(provider =>
            provider.GetRequiredService<ModuleDataPlanner>());
        services.AddSingleton<IFunctionIndexResolverFactory,
            FunctionIndexResolverFactory>();
        services.AddSingleton<IModuleExportCollector, ModuleExportCollector>();
        services.AddSingleton<IModuleExportValidator, ModuleExportValidator>();
        services.AddSingleton<IModuleImportCollector, ModuleImportCollector>();
        services.AddSingleton<WasmModuleTargetFactory>();
        services.AddSingleton<IWasmModuleTargetFactory>(provider =>
            provider.GetRequiredService<WasmModuleTargetFactory>());
        services.AddSingleton<ICanonicalAbiTypeFlattener, CanonicalAbiTypeFlattener>();
        services.AddSingleton<ICanonicalAbiSignaturePlanner, CanonicalAbiSignaturePlanner>();
        services.AddSingleton<ICanonicalAbiFunctionTypePlanner,
            CanonicalAbiFunctionTypePlanner>();
        return services;
    }
}
