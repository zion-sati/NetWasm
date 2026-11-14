namespace NetWasm.Compiler.Tests.Correctness;

// Every enum's IConvertible, Equals, GetHashCode, ToString, CompareTo and GetTypeCode is
// Enum's, so a dispatch over many boxed enums emits Enum's code once, not once per enum type.
// With a case for each, Convert.ChangeType's dispatch grew with the square of the enum count
// and failed the 7,654,321-byte function limit at about 115 enums (issue 222).
public sealed class ManyEnumDispatchCompilationTests(CorrectnessTestRunner runner)
{
    private const int EnumCount = 200;

    // Convert.ChangeType reaches string's IConvertible.ToDateTime, and date parsing reads the local time zone (as
    // upstream's does), so the program needs the platform world.
    [Fact]
    public void ManyBoxedEnumsDispatchThroughEnumsCodeOnce() =>
        runner.Run(new("ManyEnumDispatch", "ManyEnumDispatch", Source, [0, 1, 2, 3, 4, 5, 6])
        {
            Matrix = new(CorpusMatrixProfile.Family),
            RequiresReactor = true,
        });

    private static readonly string Source = $$"""
        using System;
        namespace ManyEnumDispatch;

        public static class EntryPoint
        {
            private static readonly object[] Values = { {{string.Join(", ", Enumerable.Range(0, EnumCount).Select(index => $"E{index}.A"))}} };
            private static readonly object[] Others = { {{string.Join(", ", Enumerable.Range(0, EnumCount).Select(index => $"E{index}.B"))}} };

            public static int Trace() => 0;

            public static int Run(int input)
            {
                long sum = 0;
                for (var index = 0; index < Values.Length; index++)
                {
                    var value = Values[index];
                    var other = Others[index];
                    sum = unchecked(sum * 31 + input switch
                    {
                        0 => (long)Convert.ChangeType(value, typeof(long)) + (long)(double)Convert.ChangeType(other, typeof(double)),
                        1 => ((IConvertible)value).ToInt64(null) + (long)((IConvertible)other).ToDecimal(null),
                        2 => value.Equals(other) ? 1 : value.Equals(Values[index]) ? 2 : 3,
                        3 => value.GetHashCode() ^ other.GetHashCode(),
                        4 => value.ToString()!.Length + other.ToString()!.Length,
                        5 => ((IComparable)value).CompareTo(other) * 7 + ((IComparable)other).CompareTo(value),
                        _ => (int)((IConvertible)value).GetTypeCode() + (int)Convert.GetTypeCode(other),
                    });
                }
                return (int)(sum ^ (sum >> 32));
            }
        }

        {{string.Join("\n", Enumerable.Range(0, EnumCount).Select(index => (index % 4) switch
        {
            0 => $"public enum E{index} {{ A = {index}, B }}",
            1 => $"public enum E{index} : byte {{ A = {index % 200}, B }}",
            2 => $"public enum E{index} : long {{ A = {index}L << 33, B }}",
            _ => $"public enum E{index} : short {{ A = -{index}, B }}",
        }))}}
        """;
}
