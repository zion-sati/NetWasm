namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCorpusRunner(
    IDifferentialCorpusRunner differential,
    IGeneratedFailureArtifactWriter failures,
    ISourceFailureReducer sourceReducer) : IGeneratedCorpusRunner
{
    public void Run(GeneratedCorpusCase generatedCase)
    {
        ArgumentNullException.ThrowIfNull(generatedCase);
        try
        {
            differential.Run(generatedCase.Fixture);
        }
        catch (Exception exception)
        {
            var fingerprint = Fingerprint(exception);
            var reduction = sourceReducer.Reduce(new(
                generatedCase.Fixture.Source,
                candidate => PreservesFailure(candidate, fingerprint),
                TimeSpan.FromSeconds(30)));
            var artifact = failures.Write(generatedCase, exception, reduction);
            throw new InvalidOperationException(
                $"generated case {generatedCase.Name} failed; reproduction: {artifact}",
                exception);
        }

        bool PreservesFailure(string candidate, string expected)
        {
            try
            {
                differential.Run(generatedCase.Fixture with { Source = candidate });
                return false;
            }
            catch (Exception candidateFailure)
            {
                return StringComparer.Ordinal.Equals(
                    Fingerprint(candidateFailure), expected);
            }
        }
    }

    private static string Fingerprint(Exception exception)
    {
        var message = exception.Message;
        var lineEnd = message.IndexOfAny(['\r', '\n']);
        return $"{exception.GetType().FullName}:{(lineEnd < 0 ? message : message[..lineEnd])}";
    }
}
