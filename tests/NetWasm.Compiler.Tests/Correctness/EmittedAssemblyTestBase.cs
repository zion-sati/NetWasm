namespace NetWasm.Compiler.Tests.Correctness;

public abstract class EmittedAssemblyTestBase(CorrectnessTestRunner runner)
{
    private protected void Run(CorpusCaseManifest manifest, string cell, IEmittedAssemblyBuilder builder, int? input = null) =>
        runner.Run(manifest, cell, builder, input);
}
