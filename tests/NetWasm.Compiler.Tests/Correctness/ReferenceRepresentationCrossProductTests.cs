namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ReferenceRepresentationCrossProductTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ReferenceRepresentationsMatchDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "ReferenceRepresentationCrossProduct",
            "NetWasm.Correctness.ReferenceRepresentations",
            """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.ReferenceRepresentations;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    1 => ExerciseReferenceArray(),
                    2 => ExerciseInlineArray2(),
                    3 => ExerciseInlineArray3(),
                    4 => ExerciseReadOnlySpan(),
                    5 => ExerciseSpanCopy(),
                    6 => ExerciseSpanToArray(),
                    7 => string.Concat(Runtime("a"), Runtime("bb")).Length,
                    8 => string.Concat(Runtime("a"), Runtime("bb"), Runtime("ccc")).Length,
                    9 => string.Concat(Runtime("a"), Runtime("bb"), Runtime("ccc"), Runtime("dddd")).Length,
                    10 => (Runtime("a") + Runtime("bb") + Runtime("ccc") +
                        Runtime("dddd") + Runtime("eeeee")).Length,
                    11 => new ValueModel(input).Format().Length,
                    12 => new ValueModel(input).ToString().Length,
                    _ => -1,
                };

                private static int ExerciseReferenceArray()
                {
                    var values = new string?[3];
                    values[0] = Runtime("a");
                    values[1] = Runtime("bb");
                    values[2] = Runtime("ccc");
                    return values.Length * 100 + values[0]!.Length * 10 +
                        values[1]!.Length * 2 + values[2]!.Length;
                }

                private static int ExerciseInlineArray2()
                {
                    var values = new StringBuffer2();
                    values[0] = Runtime("a");
                    values[1] = Runtime("bb");
                    return values[0].Length * 10 + values[1].Length;
                }

                private static int ExerciseInlineArray3()
                {
                    var values = new StringBuffer3();
                    values[0] = Runtime("a");
                    values[1] = Runtime("bb");
                    values[2] = Runtime("ccc");
                    return values[0].Length * 100 + values[1].Length * 10 +
                        values[2].Length;
                }

                private static int ExerciseReadOnlySpan()
                {
                    ReadOnlySpan<string?> values =
                        [Runtime("a"), Runtime("bb"), Runtime("ccc")];
                    return values[0]!.Length * 100 + values[1]!.Length * 10 +
                        values[2]!.Length;
                }

                private static int ExerciseSpanCopy()
                {
                    ReadOnlySpan<string?> values =
                        [Runtime("a"), Runtime("bb"), Runtime("ccc")];
                    var copy = new string?[values.Length];
                    values.CopyTo(copy);
                    return copy[0]!.Length * 100 + copy[1]!.Length * 10 +
                        copy[2]!.Length;
                }

                private static int ExerciseSpanToArray()
                {
                    ReadOnlySpan<string?> values =
                        [Runtime("a"), Runtime("bb"), Runtime("ccc")];
                    var copy = values.ToArray();
                    return copy.Length * 100 + copy[0]!.Length * 10 +
                        copy[2]!.Length;
                }

                private static string Runtime(string value) =>
                    value.Substring(0, value.Length);

                [InlineArray(2)]
                private struct StringBuffer2
                {
                    private string _element0;
                }

                [InlineArray(3)]
                private struct StringBuffer3
                {
                    private string _element0;
                }

                private readonly struct ValueModel(int value)
                {
                    public string Format() => value.ToString();

                    public override string ToString() => Format();
                }

                public static int Trace() => 0;
            }
            """,
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
