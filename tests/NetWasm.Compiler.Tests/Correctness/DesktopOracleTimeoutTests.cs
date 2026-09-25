namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DesktopOracleTimeoutTests
{
    [Fact]
    public void InfiniteDesktopFixtureBecomesATimedOutObservation()
    {
        var discovered = CompilerCorrectnessEnvironment.Discover();
        var environment = discovered with
        {
            ProcessTimeout = TimeSpan.FromMilliseconds(250),
        };
        var processes = new QualifiedProcessRunner();
        var compilation = new RoslynCorpusCompiler(
            discovered,
            processes,
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier())).Compile(
            new(
                "DesktopTimeout",
                "NetWasm.Correctness.DesktopTimeout",
                """
                namespace NetWasm.Correctness.DesktopTimeout;

                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        while (true) { }
                    }

                    public static int Trace() => 0;
                }
                """,
                [0]),
            CilProfile.Release,
            CorrectnessTestAssets.CreateDirectory());

        var observation = new DesktopOracleRunner(
            environment,
            processes,
            new DesktopOracleProgressReporter(TextWriter.Null))
            .Run(compilation)[0];

        Assert.Equal(OracleObservationKind.TimedOut, observation.Kind);
        Assert.Empty(observation.TraceRecords);
    }
}
