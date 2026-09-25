using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorrectnessServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompilerCorrectnessHarnessRegistersEveryNarrowService()
    {
        using var services = CorrectnessTestAssets.CreateServices();

        Assert.IsType<RoslynCorpusCompiler>(
            services.GetRequiredService<IRoslynCorpusCompiler>());
        Assert.IsType<CorpusRunDirectoryFactory>(
            services.GetRequiredService<ICorpusRunDirectoryFactory>());
        Assert.IsType<CorpusRunDirectoryCleaner>(
            services.GetRequiredService<ICorpusRunDirectoryCleaner>());
        Assert.IsType<EmbeddedCorpusSourceReader>(
            services.GetRequiredService<ICorpusSourceReader>());
        Assert.IsType<CorpusSourceNamesVerifier>(
            services.GetRequiredService<ICorpusSourceNamesVerifier>());
        Assert.IsType<CorpusSourceArtifactWriter>(
            services.GetRequiredService<ICorpusSourceArtifactWriter>());
        Assert.IsType<PatchedCorpusCompilationBuilder>(
            services.GetRequiredService<IPatchedCorpusCompilationBuilder>());
        Assert.IsType<CorpusSourceFingerprint>(
            services.GetRequiredService<ICorpusSourceFingerprint>());
        Assert.IsType<FileCorpusRunner>(
            services.GetRequiredService<IFileCorpusRunner>());
        Assert.IsType<CorpusAssemblyWriter>(
            services.GetRequiredService<ICorpusAssemblyWriter>());
        Assert.IsType<EmittedCorpusRunner>(
            services.GetRequiredService<IEmittedCorpusRunner>());
        Assert.IsType<CorpusCaseManifestParser>(services.GetRequiredService<ICorpusCaseManifestParser>());
        Assert.IsType<CorpusCaseManifestVerifier>(services.GetRequiredService<ICorpusCaseManifestVerifier>());
        Assert.IsType<CorpusCaseCatalogBuilder>(services.GetRequiredService<ICorpusCaseCatalogBuilder>());
        Assert.IsType<CorpusCaseFixtureFactory>(services.GetRequiredService<ICorpusCaseFixtureFactory>());
        Assert.IsType<RegisteredCorpusRunner>(services.GetRequiredService<IRegisteredCorpusRunner>());
        Assert.Same(CorpusCaseTestData.Catalog, services.GetRequiredService<CorpusCaseCatalog>());
        Assert.Same(CorpusCaseTestData.Assets.Features, services.GetRequiredService<CorpusFeatureCatalog>());
        Assert.IsType<DesktopOracleRunner>(
            services.GetRequiredService<IDesktopOracleRunner>());
        Assert.IsType<NetWasmOracleRunner>(
            services.GetRequiredService<INetWasmOracleRunner>());
        Assert.IsType<LinkedCorpusObservationProcess>(
            services.GetRequiredService<ILinkedCorpusObservationProcess>());
        Assert.IsType<LinkedCorpusOracleRunner>(
            services.GetRequiredService<ILinkedCorpusOracleRunner>());
        Assert.IsType<LinkedCorpusQualificationReceiptWriter>(
            services.GetRequiredService<ILinkedCorpusQualificationReceiptWriter>());
        Assert.IsType<LinkedCorpusReceiptDestinationValidator>(
            services.GetRequiredService<ILinkedCorpusReceiptDestinationValidator>());
        Assert.IsType<OracleComparer>(
            services.GetRequiredService<IOracleComparer>());
        Assert.IsType<CompilerFailureArtifactWriter>(
            services.GetRequiredService<ICompilerFailureArtifactWriter>());
        Assert.IsType<CorpusReplayCommandFormatter>(
            services.GetRequiredService<ICorpusReplayCommandFormatter>());
        Assert.IsType<CorpusCompilerProcessVerifier>(services.GetRequiredService<ICorpusCompilerProcessVerifier>());
        Assert.IsType<CorpusCompilerFailureWriter>(services.GetRequiredService<ICorpusCompilerFailureWriter>());
        Assert.IsType<CorpusCompilerResponseParser>(services.GetRequiredService<ICorpusCompilerResponseParser>());
        Assert.IsType<CorpusCompilerResponseReader>(services.GetRequiredService<ICorpusCompilerResponseReader>());
        Assert.IsType<CompilerRejectionVerifier>(services.GetRequiredService<ICompilerRejectionVerifier>());
        Assert.IsType<CompilerRejectionRunner>(services.GetRequiredService<ICompilerRejectionRunner>());
        Assert.IsType<CompilerRejectionFailureWriter>(services.GetRequiredService<ICompilerRejectionFailureWriter>());
        Assert.IsType<RandomCilRunDirectoryFactory>(services.GetRequiredService<IRandomCilRunDirectoryFactory>());
        Assert.IsType<CorpusMatrixExpander>(
            services.GetRequiredService<ICorpusMatrixExpander>());
        Assert.IsType<CorpusCompilationCellsSelector>(
            services.GetRequiredService<ICorpusCompilationCellsSelector>());
        var compiled = services.GetRequiredService<ICompiledCorpusRunner>();
        var comparison = services.GetRequiredService<ICompiledCorpusComparisonRunner>();
        Assert.IsType<CompiledCorpusRunner>(compiled);
        Assert.IsType<CompiledCorpusComparisonRunner>(comparison);
        Assert.False(compiled is ICompiledCorpusComparisonRunner);
        Assert.False(comparison is ICompiledCorpusRunner);
        Assert.Same(compiled, services.GetRequiredService<ICompiledCorpusRunner>());
        Assert.Same(comparison, services.GetRequiredService<ICompiledCorpusComparisonRunner>());
        Assert.IsType<DifferentialCorpusRunner>(
            services.GetRequiredService<IDifferentialCorpusRunner>());
        Assert.IsType<PairwiseCoveringArray>(
            services.GetRequiredService<IPairwiseCoveringArray>());
        Assert.Equal(6, services.GetServices<IGeneratedCorpusTemplate>().Count());
        Assert.IsType<LanguageCorpusGenerator>(
            services.GetRequiredService<ILanguageCorpusGenerator>());
        Assert.IsType<GeneratedFailureArtifactWriter>(
            services.GetRequiredService<IGeneratedFailureArtifactWriter>());
        Assert.IsType<GeneratedCorpusRunner>(
            services.GetRequiredService<IGeneratedCorpusRunner>());
        Assert.IsType<SourceFailureReducer>(
            services.GetRequiredService<ISourceFailureReducer>());
    }
}
