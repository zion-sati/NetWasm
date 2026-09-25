namespace NetWasm.Compiler.Tests.Correctness;

internal interface IPatchedCorpusCompilationBuilder
{
    CorpusCompilation Build(CorpusCompilation template, CorpusFixture fixture,
        CorpusArtifact artifact, CorpusSourceArtifact source, string directory);
}

internal sealed class PatchedCorpusCompilationBuilder : IPatchedCorpusCompilationBuilder
{
    public CorpusCompilation Build(CorpusCompilation template, CorpusFixture fixture,
        CorpusArtifact artifact, CorpusSourceArtifact source, string directory)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return template with
        {
            Fixture = fixture,
            Desktop = artifact,
            NetWasm = artifact,
            Sources = [source],
            Directory = directory,
        };
    }
}
