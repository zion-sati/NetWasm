namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArgumentValidationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ThrowHelpersPreserveCallerExpressionsAndExplicitNames() =>
        runner.Run(new("CallerArgumentContracts", "CallerArgumentContracts", Source, [42])
        {
            AllowUnsafe = true,
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    internal const string Source = """
        using System;
        namespace CallerArgumentContracts;
        public static unsafe class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                string? missing = null;
                string empty = "";
                string white = " ";
                int zero = 0;
                int negative = -1;
                int positive = 1;
                Check(() => ArgumentNullException.ThrowIfNull(missing), "missing");
                Check(() => ArgumentNullException.ThrowIfNull((void*)0), "(void*)0");
                Check(() => ArgumentException.ThrowIfNullOrEmpty(empty), "empty");
                Check(() => ArgumentException.ThrowIfNullOrWhiteSpace(white), "white");
                Check(() => ArgumentOutOfRangeException.ThrowIfZero(zero), "zero");
                Check(() => ArgumentOutOfRangeException.ThrowIfNegative(negative), "negative");
                Check(() => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zero), "zero");
                Check(() => ArgumentOutOfRangeException.ThrowIfEqual(positive, 1), "positive");
                Check(() => ArgumentOutOfRangeException.ThrowIfNotEqual(positive, 2), "positive");
                Check(() => ArgumentOutOfRangeException.ThrowIfGreaterThan(positive, 0), "positive");
                Check(() => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(positive, 1), "positive");
                Check(() => ArgumentOutOfRangeException.ThrowIfLessThan(negative, 0), "negative");
                Check(() => ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(negative, -1), "negative");
                Check(() => ArgumentOutOfRangeException.ThrowIfNegative(zero - positive), "zero - positive");
                Check(() => ArgumentNullException.ThrowIfNull(missing, "override"), "override");
                Check(() => ArgumentNullException.ThrowIfNull(missing, null), null);
                Check(() => ArgumentOutOfRangeException.ThrowIfZero(zero, "override"), "override");
                Check(() => ArgumentOutOfRangeException.ThrowIfZero(zero, null), null);
                ArgumentNullException.ThrowIfNull(white);
                ArgumentOutOfRangeException.ThrowIfNegative(positive);
                return input;
            }
            private static void Check(Action action, string? expected)
            {
                try { action(); }
                catch (ArgumentException exception)
                {
                    if (exception.ParamName == expected) return;
                    throw new Exception("Incorrect caller argument name");
                }
                throw new Exception("Expected argument exception");
            }
        }
        """;
}
