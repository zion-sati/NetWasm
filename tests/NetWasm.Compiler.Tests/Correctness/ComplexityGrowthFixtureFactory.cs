using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record ComplexityGrowthFixture(
    int Size,
    CorpusFixture Fixture);

internal interface IComplexityGrowthFixtureFactory
{
    ImmutableArray<ComplexityGrowthFixture> Create();
}

internal sealed class ComplexityGrowthFixtureFactory :
    IComplexityGrowthFixtureFactory
{
    private static readonly ImmutableArray<int> Sizes = [2, 4, 8, 16, 32];

    public ImmutableArray<ComplexityGrowthFixture> Create() =>
        [.. Sizes.Select(Create)];

    private static ComplexityGrowthFixture Create(int size)
    {
        var namespaceName = $"NetWasm.Correctness.Complexity.Growth{size}";
        var source = $$"""
            using System.Threading.Tasks;

            namespace {{namespaceName}};

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    var result = Diamond(input) + SwitchFanIn(input) +
                        NestedFinally(input) + Irreducible(input) + Async(input).Result;
                    _trace = result;
                    return result;
                }

                public static int Trace() => _trace;

                private static int Diamond(int input)
                {
                    var value = input;
            {{Indent(DiamondBody(size), 8)}}
                    return value;
                }

                private static int SwitchFanIn(int input)
                {
                    var value = input;
                    switch ((input & 0x7fffffff) % {{size}})
                    {
            {{Indent(SwitchBody(size), 12)}}
                    }
                Join:
                    return value + {{size}};
                }

                private static int NestedFinally(int input)
                {
                    var value = input;
            {{Indent(NestedFinallyBody(size, 0), 8)}}
                Exit:
                    return value;
                }

                private static int Irreducible(int input)
                {
                    var value = input;
                    var steps = 0;
                    if ((input & 1) == 0) goto L0;
                    goto L1;
            {{Indent(IrreducibleBody(size), 8)}}
                Exit:
                    return value;
                }

                private static async Task<int> Async(int input)
                {
                    var value = input;
            {{Indent(AsyncBody(size), 8)}}
                    return value;
                }
            }
            """;
        source = source.ReplaceLineEndings("\n");
        return new(size, new(
            $"ComplexityGrowth{size}",
            namespaceName,
            source,
            [3])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
        });
    }

    private static string DiamondBody(int size)
    {
        var source = new StringBuilder();
        for (var index = 0; index < size; index++)
        {
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"if (((value + {index}) & 1) == 0)");
            source.AppendLine("{");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"    value = unchecked(value + {index + 1});");
            source.AppendLine("}");
            source.AppendLine("else");
            source.AppendLine("{");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"    value = unchecked(value - {index + 1});");
            source.AppendLine("}");
        }
        return source.ToString();
    }

    private static string SwitchBody(int size)
    {
        var source = new StringBuilder();
        for (var index = 0; index < size; index++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"case {index}:");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"    value = unchecked(value + {index + 1});");
            source.AppendLine("    goto Join;");
        }
        return source.ToString();
    }

    private static string NestedFinallyBody(int size, int depth)
    {
        if (depth == size)
        {
            return "if ((input & 1) == 0) goto Exit;\nvalue = unchecked(value + 1);";
        }
        return $$"""
            try
            {
            {{Indent(NestedFinallyBody(size, depth + 1), 4)}}
            }
            finally
            {
                value = unchecked(value + {{depth + 3}});
            }
            """;
    }

    private static string IrreducibleBody(int size)
    {
        var source = new StringBuilder();
        for (var index = 0; index < size; index++)
        {
            var next = (index + 1) % size;
            var alternate = (index + 2) % size;
            source.AppendLine(CultureInfo.InvariantCulture, $"L{index}:");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"    value = unchecked(value + {index + 1});");
            source.AppendLine("    steps++;");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"    if (steps >= {size + 2}) goto Exit;");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"    if (((value + steps) & 1) == 0) goto L{next};");
            source.AppendLine(CultureInfo.InvariantCulture, $"    goto L{alternate};");
        }
        return source.ToString();
    }

    private static string AsyncBody(int size)
    {
        var source = new StringBuilder();
        for (var index = 0; index < size; index++)
        {
            source.AppendLine("await Task.CompletedTask;");
            source.AppendLine(
                CultureInfo.InvariantCulture,
                $"value = unchecked(value + {index + 1});");
        }
        return source.ToString();
    }

    private static string Indent(string value, int spaces)
    {
        var indentation = new string(' ', spaces);
        return string.Join(
            "\n",
            value.ReplaceLineEndings("\n").Split('\n')
                .Select(line => indentation + line));
    }
}
