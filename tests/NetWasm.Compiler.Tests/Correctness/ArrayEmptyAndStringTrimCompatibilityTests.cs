namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArrayEmptyAndStringTrimCompatibilityTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void EmptyArraysAndStringTrimMatchDesktopAcrossProfilesAndTargets()
    {
        runner.Run(new CorpusFixture(
            "ArrayEmptyAndStringTrimCompatibility",
            "NetWasm.Correctness.ArrayEmptyAndStringTrim",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            CaptureCompilerDiagnostics = true,
            EmitStackTrace = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "Array.Empty identity and String.Trim overload behavior are ordinary CoreLib contracts.",
        }.WithFullExecutionMatrix());
    }

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.ArrayEmptyAndStringTrim;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => Bool(ReferenceEquals(Array.Empty<int>(), Array.Empty<int>()), 300),
                1 => Bool(Array.Empty<int>().Length == 0, 301),
                2 => Bool(!ReferenceEquals(Array.Empty<int>(), new int[0]), 302),
                3 => Bool(" acct-0 ".Trim() == "acct-0", 303),
                4 => Bool("acct-0".Trim() == "acct-0" &&
                    ReferenceEquals("acct-0", "acct-0".Trim()), 304),
                5 => Bool("  acct-0  ".TrimStart() == "acct-0  " &&
                    "  acct-0  ".TrimEnd() == "  acct-0", 305),
                6 => Bool("..acct-0..".Trim('.') == "acct-0", 306),
                7 => Bool("  acct-0  ".Trim((char[]?)null) == "acct-0", 307),
                8 => Bool("  acct-0  ".Trim(Array.Empty<char>()) == "acct-0", 308),
                _ => Bool("acct-0".ToUpperInvariant() == "ACCT-0" &&
                    ReferenceEquals("ACCT-0", "ACCT-0".ToUpperInvariant()), 309),
            };

            public static int Trace() => 0;

            private static int Bool(bool condition, int value) => condition ? value : -value;
        }
        """;
}
