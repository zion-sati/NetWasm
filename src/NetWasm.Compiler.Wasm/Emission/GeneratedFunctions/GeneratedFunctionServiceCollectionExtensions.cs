using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal static class GeneratedFunctionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmGeneratedFunctionEmission(
        this IServiceCollection services)
    {
        services.AddLocalTimeGeneratedFunctionEmission();
        services.AddSingleton<IStaticInitializerFunctionEmitter, StaticInitializerFunctionEmitter>();
        services.AddSingleton<IStaticInitializationFailureEmitter, StaticInitializationFailureEmitter>();
        services.AddSingleton<IStaticInitializerFunctionAppender, StaticInitializerFunctionAppender>();
        services.AddSingleton<IGeneratedFunctionWriterFactory,
            GeneratedFunctionWriterFactory>();
        services.AddSingleton<IReferenceComparisonEmitter,
            ReferenceComparisonEmitter>();
        services.AddSingleton<IDelegateLeafEqualityEmitter,
            DelegateLeafEqualityEmitter>();
        services.AddSingleton<IDelegateCountFunctionEmitter,
            DelegateCountFunctionEmitter>();
        services.AddSingleton<IDelegateLeafFunctionEmitter,
            DelegateLeafFunctionEmitter>();
        services.AddSingleton<IDelegateEqualityFunctionEmitter,
            DelegateEqualityFunctionEmitter>();
        services.AddSingleton<IDelegateRemoveFunctionEmitter,
            DelegateRemoveFunctionEmitter>();
        services.AddSingleton<IDelegateInvokeFunctionEmitter,
            DelegateInvokeFunctionEmitter>();
        services.AddSingleton<IDelegateInvocationTargetSelector,
            DelegateInvocationTargetSelector>();
        services.AddSingleton<IDelegateFunctionAppender, DelegateFunctionAppender>();
        services.AddSingleton<IRuntimeCoreInitializer, RuntimeCoreInitializer>();
        services.AddSingleton<IRuntimeStateInitializer, RuntimeStateInitializer>();
        services.AddSingleton<IFilterFuncletEmitter, FilterFuncletEmitter>();
        services.AddSingleton<IFilterSequenceEmitterFactory,
            FilterSequenceEmitterFactory>();
        services.AddSingleton<IFilterFunctionAppender, FilterFunctionAppender>();
        services.AddSingleton<IFilterFunctionSetAppender,
            FilterFunctionSetAppender>();
        services.AddSingleton<IHostCallbackFunctionEmitter,
            HostCallbackFunctionEmitter>();
        services.AddSingleton<IHostCallbackFunctionTypeResolver,
            HostCallbackFunctionTypeResolver>();
        services.AddSingleton<IHostCallbackFunctionAppender,
            HostCallbackFunctionAppender>();
        services.AddSingleton<IHostCallbackSetAppender, HostCallbackSetAppender>();
        services.AddSingleton<IAsyncJSImportResolveEmitter,
            AsyncJSImportResolveEmitter>();
        services.AddSingleton<IAsyncJSImportRejectEmitter,
            AsyncJSImportRejectEmitter>();
        services.AddSingleton<IAsyncJSImportCancelEmitter,
            AsyncJSImportCancelEmitter>();
        services.AddSingleton<IAsyncJSImportResolveTypeResolver,
            AsyncJSImportResolveTypeResolver>();
        services.AddSingleton<IAsyncJSImportHelperAppender,
            AsyncJSImportHelperAppender>();
        services.AddSingleton<IAsyncJSImportSetAppender, AsyncJSImportSetAppender>();
        services.AddSingleton<IAsyncJSExportWrapperEmitter,
            AsyncJSExportWrapperEmitter>();
        services.AddSingleton<IAsyncJSExportStatusEmitter,
            AsyncJSExportStatusEmitter>();
        services.AddSingleton<IAsyncJSExportResultEmitter,
            AsyncJSExportResultEmitter>();
        services.AddSingleton<IAsyncJSExportCompletionEmitter,
            AsyncJSExportCompletionEmitter>();
        services.AddSingleton<IAsyncJSExportResultTypeResolver,
            AsyncJSExportResultTypeResolver>();
        services.AddSingleton<IAsyncJSExportHelperAppender,
            AsyncJSExportHelperAppender>();
        services.AddSingleton<IOutwardMethodFunctionAppender,
            OutwardMethodFunctionAppender>();
        services.AddSingleton<IRequestedExportFunctionAppender,
            RequestedExportFunctionAppender>();
        services.AddSingleton<IRequestedExportSetAppender,
            RequestedExportSetAppender>();
        services.AddSingleton<IFilterDispatcherEmitter, FilterDispatcherEmitter>();
        services.AddSingleton<IFinalizerDispatcherEmitter,
            FinalizerDispatcherEmitter>();
        services.AddSingleton<IRuntimeFunctionAppender, RuntimeFunctionAppender>();
        services.AddSingleton<IEntryPointEmitter, EntryPointEmitter>();
        services.AddSingleton<IComponentBoundaryEmitter, ComponentBoundaryEmitter>();
        services.AddSingleton<IComponentFunctionAppender, ComponentFunctionAppender>();
        return services;
    }
}
