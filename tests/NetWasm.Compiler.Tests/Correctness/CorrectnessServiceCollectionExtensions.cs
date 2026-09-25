using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class CorrectnessServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerCorrectnessHarness(
        this IServiceCollection services,
        string? repositoryStartDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNetWasmCompiler();
        services.AddSingleton(CompilerCorrectnessEnvironment.Discover(repositoryStartDirectory));
        services.AddSingleton(new CorpusHostIdentity(
            AppContext.TargetFrameworkName,
            Environment.Version.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()));
        services.AddSingleton<IQualifiedProcessRunner, QualifiedProcessRunner>();
        services.AddLinkedCorpusBuild();
        services.AddSingleton<IOracleModePolicy, SameIlOracleModePolicy>();
        services.AddSingleton<IOracleModePolicy, SameSourceOracleModePolicy>();
        services.AddSingleton<IOracleModePolicy, FrozenDesktopOracleModePolicy>();
        services.AddSingleton<IOracleModePolicyRegistry, OracleModePolicyRegistry>();
        services.AddSingleton<ICorpusCompilerRequestFactory, CorpusCompilerRequestFactory>();
        services.AddSingleton<ICorpusCompilerRequestWriter, CorpusCompilerRequestWriter>();
        services.AddSingleton<ICorpusApplicationCompiler, CorpusApplicationCompiler>();
        services.AddSingleton<IRoslynCorpusCompiler, RoslynCorpusCompiler>();
        services.AddSingleton<ICorpusRunDirectoryFactory, CorpusRunDirectoryFactory>();
        services.AddSingleton<ICorpusRunDirectoryCleaner, CorpusRunDirectoryCleaner>();
        services.AddSingleton<ICorpusSourceReader, EmbeddedCorpusSourceReader>();
        services.AddSingleton<ICorpusSourceNamesVerifier, CorpusSourceNamesVerifier>();
        services.AddSingleton<ICorpusSourceArtifactWriter, CorpusSourceArtifactWriter>();
        services.AddSingleton<IPatchedCorpusCompilationBuilder, PatchedCorpusCompilationBuilder>();
        services.AddSingleton<ICorpusSourceFingerprint, CorpusSourceFingerprint>();
        services.AddSingleton<IFileCorpusRunner, FileCorpusRunner>();
        services.AddSingleton<ICorpusAssemblyWriter, CorpusAssemblyWriter>();
        services.AddSingleton<IEmittedCorpusRunner, EmittedCorpusRunner>();
        services.AddSingleton(CorpusCaseTestData.Assets.Features);
        services.AddSingleton(CorpusCaseTestData.Catalog);
        services.AddSingleton<ICorpusCaseManifestParser, CorpusCaseManifestParser>();
        services.AddSingleton<ICorpusCaseManifestVerifier, CorpusCaseManifestVerifier>();
        services.AddSingleton<ICorpusCaseCatalogBuilder, CorpusCaseCatalogBuilder>();
        services.AddSingleton<ICorpusCaseFixtureFactory, CorpusCaseFixtureFactory>();
        services.AddSingleton<IRegisteredCorpusRunner, RegisteredCorpusRunner>();
        services.AddSingleton<IDesktopOracleRunner, DesktopOracleRunner>();
        services.AddSingleton<INetWasmOracleRunner, NetWasmOracleRunner>();
        services.AddSingleton<IFrozenOracleEvidenceReader, FrozenOracleEvidenceReader>();
        services.AddSingleton<ICorpusExportFactory, CorpusExportFactory>();
        services.AddSingleton<IOracleComparer, OracleComparer>();
        services.AddSingleton<ICorpusExpectationVerifier, CorpusExpectationVerifier>();
        services.AddSingleton<ICorpusExecutionVerifier, CorpusExecutionVerifier>();
        services.AddSingleton<IOracleRuntimeCapabilityVerifier, OracleRuntimeCapabilityVerifier>();
        services.AddSingleton<ICorpusReplayCommandFormatter, CorpusReplayCommandFormatter>();
        services.AddSingleton<ICorpusCompilerProcessVerifier, CorpusCompilerProcessVerifier>();
        services.AddSingleton<ICorpusCompilerFailureWriter, CorpusCompilerFailureWriter>();
        services.AddSingleton<ICorpusCompilerResponseParser, CorpusCompilerResponseParser>();
        services.AddSingleton<ICorpusCompilerResponseReader, CorpusCompilerResponseReader>();
        services.AddSingleton<ICorpusMatrixExpander, CorpusMatrixExpander>();
        services.AddSingleton<ICompilerFailureArtifactWriter,
            CompilerFailureArtifactWriter>();
        services.AddSingleton<ICorpusCompilationCellsSelector, CorpusCompilationCellsSelector>();
        services.AddSingleton<ICompiledCorpusRunner, CompiledCorpusRunner>();
        services.AddSingleton<ICompiledCorpusComparisonRunner, CompiledCorpusComparisonRunner>();
        services.AddSingleton<IDifferentialCorpusRunner, DifferentialCorpusRunner>();
        services.AddSingleton<IPairwiseCoveringArray, PairwiseCoveringArray>();
        services.AddSingleton<IGeneratedCorpusTemplate, ControlFlowCorpusTemplate>();
        services.AddSingleton<IGeneratedCorpusTemplate, ExceptionTransferCorpusTemplate>();
        services.AddSingleton<IGeneratedCorpusTemplate, ValueCallCorpusTemplate>();
        services.AddSingleton<IGeneratedCorpusTemplate, GenericDispatchCorpusTemplate>();
        services.AddSingleton<IGeneratedCorpusTemplate, AsyncCleanupCorpusTemplate>();
        services.AddSingleton<IGeneratedCorpusTemplate, SyntaxTreeProfileCorpusTemplate>();
        services.AddSingleton<ILanguageCorpusGenerator, LanguageCorpusGenerator>();
        services.AddSingleton<IGeneratedFailureArtifactWriter,
            GeneratedFailureArtifactWriter>();
        services.AddSingleton<IGeneratedCorpusRunner, GeneratedCorpusRunner>();
        services.AddSingleton<IValidCSharpFuzzGenerator,
            ValidCSharpFuzzGenerator>();
        services.AddSingleton<ICfgPropertyCorpusGenerator,
            CfgPropertyCorpusGenerator>();
        services.AddSingleton<ICfgPropertyRunner, CfgPropertyRunner>();
        services.AddSingleton<IComplexityGrowthFixtureFactory,
            ComplexityGrowthFixtureFactory>();
        services.AddSingleton<IMalformedInputGenerator, MalformedInputGenerator>();
        services.AddSingleton<IMalformedCompilationRunner, MalformedCompilationRunner>();
        services.AddSingleton<ICompilerRejectionVerifier, CompilerRejectionVerifier>();
        services.AddSingleton<ICompilerRejectionRunner, CompilerRejectionRunner>();
        services.AddSingleton<ICompilerRejectionFailureWriter, CompilerRejectionFailureWriter>();
        services.AddSingleton<IRandomCilGenerator, RandomCilGenerator>();
        services.AddSingleton<IRandomCilInteractionMatrix,
            RandomCilInteractionMatrix>();
        services.AddSingleton<IRandomCilInteractionGenerator,
            RandomCilInteractionGenerator>();
        services.AddSingleton<IRandomCilInteractionShardPlanner,
            RandomCilInteractionShardPlanner>();
        services.AddSingleton<RitProgressCounterStore>();
        services.AddSingleton<RitProgressSnapshotWriter>();
        services.AddSingleton<IOracleOperationProgressPlan, OracleOperationProgressPlan>();
        services.AddSingleton<IOracleOperationProgressReporter, OracleOperationProgressReporter>();
        services.AddSingleton<IRandomCilCaseProgressReporter, RandomCilCaseProgressReporter>();
        services.AddSingleton<IDesktopOracleProgressReporter, DesktopOracleProgressReporter>();
        services.AddSingleton<IRandomCilInteractionFixtureFactory,
            RandomCilInteractionFixtureFactory>();
        services.AddSingleton<IRandomCilInteractionCampaign,
            RandomCilInteractionCampaign>();
        services.AddSingleton<IRandomCilRunDirectoryFactory, RandomCilRunDirectoryFactory>();
        services.AddSingleton(RandomCilCampaignOptions.CreateDefault());
        services.AddSingleton<IRandomCilCampaignProgressReporter,
            ConsoleRandomCilCampaignProgressReporter>();
        services.AddSingleton<IRandomCilCampaignScheduler,
            RandomCilCampaignScheduler>();
        services.AddSingleton<IGeneratedCilValidator, GeneratedCilValidator>();
        services.AddSingleton<IGeneratedCilSerializer, GeneratedCilSerializer>();
        services.AddSingleton<IGeneratedCilManifestWriter,
            GeneratedCilManifestWriter>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGeneratedCilReductionPass, InputReductionPass>();
        services.AddSingleton<IGeneratedCilReductionPass, MethodReductionPass>();
        services.AddSingleton<IGeneratedCilReductionPass, ConstantReductionPass>();
        services.AddSingleton<IGeneratedCilReductionPass, InstructionReductionPass>();
        services.AddSingleton<IGeneratedCilReductionPass, LocalReductionPass>();
        services.AddSingleton<IGeneratedCilReductionPass, ControlFlowReductionPass>();
        services.AddSingleton<IGeneratedCilReductionPass,
            ExceptionRegionReductionPass>();
        services.AddSingleton<IGeneratedCilReducer, GeneratedCilReducer>();
        services.AddSingleton<ISourceFailureReducer, SourceFailureReducer>();
        services.AddSingleton<IGeneratedCilFailureBundleWriter,
            GeneratedCilFailureBundleWriter>();
        services.AddSingleton<IGeneratedCilRegressionRunner,
            GeneratedCilRegressionRunner>();
        services.AddSingleton<IGeneratedCilRegressionPromoter,
            GeneratedCilRegressionPromoter>();
        services.AddSingleton<IInvalidCilReducer, InvalidCilReducer>();
        services.AddSingleton<IMethodBodyPatcher, MethodBodyPatcher>();
        services.AddSingleton<IRandomCilMetadataTokenResolver,
            RandomCilMetadataTokenResolver>();
        return services;
    }
}
