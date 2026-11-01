namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AppContextCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AppContextMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "AppContextCompatibilityCorpus",
            "NetWasm.Correctness.AppContextCompatibility",
            """
            using System;

            namespace NetWasm.Correctness.AppContextCompatibility;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (AppContext.GetData("missing") is not null ||
                        AppContext.TryGetSwitch("missing-switch", out _))
                    {
                        return -1;
                    }

                    AppContext.SetData("model", 42);
                    AppContext.SetData("text-feature", "true");
                    if (!AppContext.TryGetSwitch("text-feature", out var textEnabled) ||
                        !textEnabled)
                    {
                        return -2;
                    }

                    AppContext.SetSwitch("text-feature", false);
                    AppContext.SetSwitch("feature", true);
                    return (int)AppContext.GetData("model")! +
                        (AppContext.TryGetSwitch("feature", out var enabled) && enabled
                            ? 100
                            : 0) +
                        (AppContext.TryGetSwitch("text-feature", out textEnabled) && !textEnabled
                            ? 1
                            : 0);
                }

                public static int Trace() => 0;
            }
            """,
            [0])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
