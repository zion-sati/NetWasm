using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IFileCorpusRunner
{
    void Run(CorpusFixture fixture, string sourceFile);
    void Run(CorpusFixture fixture, ImmutableArray<string> sourceFiles);
}

internal sealed class FileCorpusRunner(
    ICorpusSourceReader sources,
    IDifferentialCorpusRunner runner,
    ICorpusSourceNamesVerifier names) : IFileCorpusRunner
{
    public void Run(CorpusFixture fixture, string sourceFile) => Run(fixture, [sourceFile]);

    public void Run(CorpusFixture fixture, ImmutableArray<string> sourceFiles)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        names.Verify(sourceFiles);
        if (fixture.Source != string.Empty || fixture.AdditionalSources.IsDefault || !fixture.AdditionalSources.IsEmpty)
        {
            throw new ArgumentException("A file-backed fixture cannot also declare inline source.", nameof(fixture));
        }
        var loaded = ImmutableArray.CreateBuilder<CorpusSourceFile>();
        foreach (var sourceFile in sourceFiles)
        {
            var source = sources.Read(sourceFile);
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidOperationException("Corpus source asset is empty.");
            }
            loaded.Add(new(sourceFile, source));
        }
        runner.Run(fixture with { Source = loaded[0].Content, AdditionalSources = [.. loaded.Skip(1)] });
    }
}
