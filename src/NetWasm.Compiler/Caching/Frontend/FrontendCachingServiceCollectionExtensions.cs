using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Caching.Frontend;

internal static class FrontendCachingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerFrontendCaching(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFrontendArtifactSnapshotter, FrontendArtifactSnapshotter>();
        services.AddSingleton<IFrontendArtifactEncoder, FrontendArtifactEncoder>();
        services.AddSingleton<IFrontendArtifactDecoder, FrontendArtifactDecoder>();
        services.AddSingleton<IFrontendArtifactEligibilityClassifier,
            FrontendArtifactEligibilityClassifier>();
        services.AddSingleton<FrontendArtifactCacheState>();
        services.AddSingleton<FrontendArtifactMemoryStore>();
        services.AddSingleton<FrontendArtifactObjectStore>();
        services.AddSingleton<FrontendArtifactTransportStore>();
        services.AddSingleton<IFrontendArtifactCacheIdentityBuilder,
            FrontendArtifactCacheIdentityBuilder>();
        services.AddSingleton<IEntryAssemblyBindingFingerprinter,
            EntryAssemblyBindingFingerprinter>();
        services.AddSingleton<IFrontendArtifactPayloadReader, FrontendArtifactPayloadReader>();
        services.AddSingleton<FrontendArtifactPayloadPublisher>();
        services.AddSingleton<FrontendArtifactTransportPublisher>();
        services.AddSingleton<IFrontendArtifactPayloadPublisher,
            FrontendArtifactPayloadPublicationFanout>();
        services.AddSingleton<IFrontendArtifactCacheTransport,
            FrontendArtifactCacheTransport>();
        services.AddSingleton<IFrontendArtifactObjectReader, FrontendArtifactObjectReader>();
        services.AddSingleton<IFrontendArtifactObjectPublisher, FrontendArtifactObjectPublisher>();
        services.AddSingleton<IFrontendArtifactCacheRequestResolver,
            FrontendArtifactCacheRequestResolver>();
        services.AddSingleton<IFrontendArtifactCacheMetricsReader,
            FrontendArtifactCacheMetricsReader>();
        services.AddSingleton<IFrontendArtifactCacheRequestFactory,
            FrontendArtifactCacheRequestFactory>();
        services.AddSingleton<IFrontendCacheMetricsObserverFactory,
            FrontendCacheMetricsObserverFactory>();
        services.AddSingleton<IFrontendArtifactHydrator>(static provider =>
            new FrontendArtifactHydrator(
                provider.GetRequiredService<IControlFlowGraphBuilderFactory>().Create(),
                provider.GetRequiredService<IStructuredMethodFactory>()));
        services.AddSingleton<IFrontendArtifactRestorer, FrontendArtifactRestorer>();
        services.AddSingleton<IFrontendAnalysisRecorder, FrontendAnalysisRecorder>();
        services.AddSingleton<IFrontendStructuredMethodRestorer,
            FrontendStructuredMethodRestorer>();
        services.AddSingleton<IFrontendArtifactStager, FrontendArtifactStager>();
        services.AddSingleton<IReachableMethodAnalyzerFactory>(static provider =>
            new CachingReachableMethodAnalyzerFactory(
                provider.GetRequiredService<ReachableMethodAnalyzerFactory>(),
                provider.GetRequiredService<IFrontendArtifactRestorer>(),
                provider.GetRequiredService<IFrontendAnalysisRecorder>()));
        services.AddSingleton<IWasmMethodLowerer>(static provider =>
            new CachingWasmMethodLowerer(
                provider.GetRequiredService<WasmMethodLowerer>(),
                provider.GetRequiredService<IFrontendStructuredMethodRestorer>(),
                provider.GetRequiredService<IFrontendArtifactStager>()));
        return services;
    }
}
