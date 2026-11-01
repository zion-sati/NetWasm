namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArrayBoundsCompatibilityTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArrayBoundsAndExceptionOrderingMatchDesktopAcrossTargets()
    {
        runner.Run(new CorpusFixture(
            "ArrayBoundsCompatibility",
            "NetWasm.Correctness.ArrayBounds",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8])
        {
            CaptureCompilerDiagnostics = true,
            EmitStackTrace = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "Array bounds and managed exception ordering are ordinary CLR contracts.",
        }.WithFullExecutionMatrix());
    }

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.ArrayBounds;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => LastValid(),
                1 => ExpectIndexFailure([11], -1, 301),
                2 => ExpectIndexFailure([], 0, 302),
                3 => ExpectIndexFailure([11], 1, 303),
                4 => ExpectIndexFailure([11], int.MinValue, 304),
                5 => ExpectIndexFailure([11], int.MaxValue, 305),
                6 => ExpectNullFailure(),
                7 => NativeLastValid(),
                _ => NativeFirstInvalid(),
            };

            public static int Trace() => 0;

            private static int LastValid()
            {
                int[] values = [7, 13, 29];
                return values[2] == 29 ? 300 : -300;
            }

            private static int ExpectIndexFailure(
                int[] values,
                int index,
                int success)
            {
                try
                {
                    _ = values[index];
                    return -success;
                }
                catch (IndexOutOfRangeException)
                {
                    return success;
                }
            }

            private static int ExpectNullFailure()
            {
                int[]? values = null;
                try
                {
                    _ = values![0];
                    return -306;
                }
                catch (NullReferenceException)
                {
                    return 306;
                }
            }

            private static int NativeLastValid()
            {
                int[] values = [31, 37];
                nint index = 1;
                return values[index] == 37 ? 307 : -307;
            }

            private static int NativeFirstInvalid()
            {
                int[] values = [31, 37];
                nint index = 2;
                try
                {
                    _ = values[index];
                    return -308;
                }
                catch (IndexOutOfRangeException)
                {
                    return 308;
                }
            }
        }
        """;
}
