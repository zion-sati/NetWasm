namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class UnsafeByReferenceDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleaseByReferencePointerAndSpanCodeMatchesDesktopDotNet()
    {
        runner.Run(new(
            "UnsafeByReferenceDifferential",
            "NetWasm.Correctness.UnsafeCode",
            """
            namespace NetWasm.Correctness.UnsafeCode;

            public static class EntryPoint
            {
                private static int _trace;

                public static unsafe int Run(int input)
                {
                    _trace = 1;
                    var values = new[] { input, 2, 3 };
                    fixed (int* pointer = values)
                    {
                        pointer[1] += pointer[0];
                        *(pointer + 2) += 4;
                    }

                    System.Span<int> span = stackalloc int[3];
                    span[0] = values[0];
                    span[1] = values[1];
                    span[2] = values[2];
                    ref var alias = ref span[1];
                    Increment(ref alias);
                    ReadAndWrite(in alias, out var copied);
                    _trace = span[0] + span[1] + span[2] + copied;
                    return _trace;
                }

                public static int Trace() => _trace;

                private static void Increment(ref int value) => value++;

                private static void ReadAndWrite(in int value, out int result) =>
                    result = value;
            }
            """,
            [-1, 0, 1, 41])
        {
            AllowUnsafe = true,
        });
    }
}
