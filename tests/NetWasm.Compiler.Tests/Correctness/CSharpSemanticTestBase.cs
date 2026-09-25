namespace NetWasm.Compiler.Tests.Correctness;

public abstract class CSharpSemanticTestBase(CorrectnessTestRunner runner)
{
    private protected void Run(CorpusCaseManifest manifest, string cell) => runner.Run(manifest, cell);

    private protected void Run(CorpusFixture fixture) => runner.Run(fixture);
}
