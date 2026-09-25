namespace NetWasm.Compiler.Tests.Correctness;

public abstract class CompilerRejectionTestBase(CorrectnessTestRunner runner)
{
    private protected void Run(CompilerRejectionCase testCase) => runner.RunRejection(testCase);
}
