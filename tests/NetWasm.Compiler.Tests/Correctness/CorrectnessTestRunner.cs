using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CorrectnessTestGroup : ICollectionFixture<CorrectnessTestRunner>
{
    public const string Name = "compiler-correctness";
}

public sealed class CorrectnessTestRunner : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddCompilerCorrectnessHarness()
        .BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

    internal void Run(CorpusFixture fixture) =>
        _services.GetRequiredService<IDifferentialCorpusRunner>().Run(fixture);

    internal void RunRejection(CompilerRejectionCase testCase) =>
        _services.GetRequiredService<ICompilerRejectionRunner>().Run(testCase);

    internal void Run(CorpusCaseManifest manifest, string cell) =>
        _services.GetRequiredService<IRegisteredCorpusRunner>().Run(manifest, cell);

    internal void Run(CorpusCaseManifest manifest, string cell, IEmittedAssemblyBuilder builder, int? input = null) =>
        _services.GetRequiredService<IRegisteredCorpusRunner>().Run(manifest, cell, builder, input);

    internal IReadOnlyList<GeneratedCorpusCase> GeneratedCases =>
        _services.GetRequiredService<ILanguageCorpusGenerator>()
            .GeneratePullRequestCorpus();

    internal void RunGenerated(GeneratedCorpusCase generatedCase) =>
        _services.GetRequiredService<IGeneratedCorpusRunner>().Run(generatedCase);

    internal IReadOnlyList<GeneratedCorpusCase> GenerateFuzzCases(
        params int[] seeds) => _services
        .GetRequiredService<IValidCSharpFuzzGenerator>()
        .Generate([.. seeds]);

    internal IReadOnlyList<CfgPropertyCase> GenerateCfgProperties(
        params int[] seeds) => _services
        .GetRequiredService<ICfgPropertyCorpusGenerator>()
        .Generate([.. seeds]);

    internal void RunCfgProperty(CfgPropertyCase property) =>
        _services.GetRequiredService<ICfgPropertyRunner>().Run(property);

    public void Dispose() => _services.Dispose();
}
