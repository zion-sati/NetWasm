using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class LinkedCorpusServiceCollectionExtensions
{
    public static IServiceCollection AddLinkedCorpusBuild(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ILinkedCorpusBuildPlanFactory, LinkedCorpusBuildPlanFactory>();
        services.AddSingleton<ILinkedCorpusBuildExecutor, LinkedCorpusBuildExecutor>();
        services.AddSingleton(new LinkedCorpusToolEnvironment(
            Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT"),
            Environment.GetEnvironmentVariable("NETWASM_NODE_PATH"),
            Environment.GetEnvironmentVariable("NETWASM_WASM_TOOLS_PATH")));
        services.AddSingleton<ILinkedCorpusToolPathsProvider,
            LinkedCorpusToolPathsProvider>();
        services.AddSingleton<ICorpusArtifactFingerprint,
            CorpusArtifactFingerprint>();
        services.AddSingleton<ILinkedCorpusObservationRequestFactory,
            LinkedCorpusObservationRequestFactory>();
        services.AddSingleton<ILinkedCorpusObservationRequestWriter,
            LinkedCorpusObservationRequestWriter>();
        services.AddSingleton<ILinkedCorpusObservationResponseParser,
            LinkedCorpusObservationResponseParser>();
        services.AddSingleton<ILinkedCorpusObservationResponseReader,
            LinkedCorpusObservationResponseReader>();
        services.AddSingleton<ILinkedCorpusObservationProcess,
            LinkedCorpusObservationProcess>();
        services.AddSingleton<ILinkedCorpusOracleRunner,
            LinkedCorpusOracleRunner>();
        services.AddSingleton(new LinkedCorpusReceiptEnvironment(
            Environment.GetEnvironmentVariable("NETWASM_CORPUS_RECEIPTS")));
        services.AddSingleton<ILinkedCorpusReceiptDestinationValidator,
            LinkedCorpusReceiptDestinationValidator>();
        services.AddSingleton<ILinkedCorpusQualificationReceiptWriter,
            LinkedCorpusQualificationReceiptWriter>();
        return services;
    }
}
