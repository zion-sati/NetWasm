namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ExtendedLoopOwnershipCompatibilityTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void SharedSideEffectingExitTailExecutesOnceAcrossProfilesAndTargets()
    {
        runner.Run(new CorpusFixture(
            "ExtendedLoopOwnershipCompatibility",
            "NetWasm.Correctness.ExtendedLoopOwnership",
            Source,
            [-1, 0, 1, 2, 3])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "Structured loop replicas must preserve ordinary C# control-flow side effects.",
        }.WithFullExecutionMatrix());
    }

    [Fact]
    public void SharedExitTailRunsAfterFinallyCleanupAcrossProfilesAndTargets()
    {
        runner.Run(new CorpusFixture(
            "ExtendedLoopExceptionOwnershipCompatibility",
            "NetWasm.Correctness.ExtendedLoopExceptionOwnership",
            ExceptionSource,
            [-1, 0, 1, 2, 3])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "Structured loop exit replicas must preserve C# finally ordering and exactly-once side effects.",
        }.WithFullExecutionMatrix());
    }

    private const string Source = """
        namespace NetWasm.Correctness.ExtendedLoopOwnership;

        public static class EntryPoint
        {
            private static int _trace;

            public static int Run(int input)
            {
                _trace = 0;
                var value = 0;
                if (input < 0)
                {
                    goto SharedTail;
                }

            Loop:
                if (value >= input)
                {
                    goto ConditionTail;
                }
                value++;
                if (value == 2)
                {
                    goto SharedTail;
                }
                goto Loop;

            ConditionTail:
                _trace = unchecked(_trace * 31 + 7);
                value += 100;
                goto Exit;

            SharedTail:
                _trace = unchecked(_trace * 31 + 11);
                value += 10;

            Exit:
                _trace = unchecked(_trace * 31 + 13);
                return value;
            }

            public static int Trace() => _trace;
        }
        """;

    private const string ExceptionSource = """
        namespace NetWasm.Correctness.ExtendedLoopExceptionOwnership;

        public static class EntryPoint
        {
            private static int _trace;

            public static int Run(int input)
            {
                _trace = 0;
                var value = 0;
                if (input < 0)
                {
                    goto SharedTail;
                }

                try
                {
                Loop:
                    if (value >= input)
                    {
                        goto ConditionTail;
                    }
                    value++;
                    if (value == 2)
                    {
                        goto SharedTail;
                    }
                    goto Loop;
                }
                finally
                {
                    Record(5);
                }

            ConditionTail:
                Record(7);
                value += 100;
                goto Exit;

            SharedTail:
                Record(11);
                value += 10;

            Exit:
                Record(13);
                return value;
            }

            public static int Trace() => _trace;

            private static void Record(int marker) =>
                _trace = unchecked(_trace * 31 + marker);
        }
        """;
}
