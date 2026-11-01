namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DeterministicGeneratedCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PullRequestCorpusMatchesDesktopDotNet()
    {
        var requested = Environment.GetEnvironmentVariable("NETWASM_GENERATED_CASE");
        var cases = string.IsNullOrEmpty(requested)
            ? runner.GeneratedCases
            : runner.GeneratedCases.Where(item => item.Name == requested).ToArray();
        if (!string.IsNullOrEmpty(requested))
        {
            Assert.Single(cases);
        }
        foreach (var generatedCase in cases)
        {
            runner.RunGenerated(generatedCase);
        }
    }
}
