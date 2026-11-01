using Microsoft.Extensions.DependencyInjection;

// One harness for the whole assembly: each correctness class is its own
// collection, so the classes run in parallel and share the harness's caches.
[assembly: AssemblyFixture(typeof(NetWasm.Compiler.Tests.Correctness.CorrectnessTestRunner))]

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorrectnessTestRunner : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddCompilerCorrectnessHarness()
        .BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

    internal void Run(CorpusFixture fixture)
    {
        if (Applies(fixture.Name, fixture.RequiredRuntimeCapabilities))
            _services.GetRequiredService<IDifferentialCorpusRunner>().Run(fixture);
    }

    internal void Run(CorpusFixture fixture, IEmittedAssemblyBuilder builder)
    {
        if (Applies(fixture.Name, fixture.RequiredRuntimeCapabilities))
            _services.GetRequiredService<IEmittedCorpusRunner>().Run(fixture, builder);
    }

    internal void RunRejection(CompilerRejectionCase testCase) =>
        _services.GetRequiredService<ICompilerRejectionRunner>().Run(testCase);

    internal void Run(CorpusCaseManifest manifest, string cell)
    {
        if (Applies(manifest.Name, manifest.RequiredRuntimeCapabilities))
            _services.GetRequiredService<IRegisteredCorpusRunner>().Run(manifest, cell);
    }

    internal void Run(CorpusCaseManifest manifest, string cell, IEmittedAssemblyBuilder builder, int? input = null)
    {
        if (Applies(manifest.Name, manifest.RequiredRuntimeCapabilities))
            _services.GetRequiredService<IRegisteredCorpusRunner>().Run(manifest, cell, builder, input);
    }

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

    // A fixture that needs a capability the selected linked collector lacks,
    // such as garbage collection under Bump, cannot run there; it is reported
    // as not applicable instead of failing.
    private bool Applies(string name, OracleRuntimeCapabilities required)
    {
        var collector = _services.GetRequiredService<LinkedCorpusToolEnvironment>().Collector;
        var missing = required & LinkedCorpusCollectorCapabilities.Collecting &
            ~LinkedCorpusCollectorCapabilities.Of(collector);
        if (missing == OracleRuntimeCapabilities.None) return true;
        Console.WriteLine($"Not applicable under the {collector} collector: {name} requires {missing}.");
        return false;
    }

    public void Dispose() => _services.Dispose();
}
