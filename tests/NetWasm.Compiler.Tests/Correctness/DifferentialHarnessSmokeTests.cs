namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DifferentialHarnessSmokeTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void HarnessComparesValuesExceptionsAndTraceForDebugAndReleaseCil()
    {
        Run(new(
            "DifferentialHarnessSmoke",
            "NetWasm.Correctness.Smoke",
            """
            namespace NetWasm.Correctness.Smoke;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = input * 10;
                    if (input < 0)
                    {
                        throw new System.ArgumentOutOfRangeException();
                    }
                    _trace++;
                    return input + 1;
                }

                public static int Trace() => _trace;
            }
            """,
            [41, -1]));
    }
}
