namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusSourceReader
{
    string Read(string sourceFile);
}

internal sealed class EmbeddedCorpusSourceReader : ICorpusSourceReader
{
    public string Read(string sourceFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        using var stream = typeof(EmbeddedCorpusSourceReader).Assembly.GetManifestResourceStream(
            "Correctness/Fixtures/" + sourceFile)
            ?? throw new FileNotFoundException("Corpus source asset was not found.", sourceFile);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
