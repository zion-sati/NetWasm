using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ControlFlowCorpusTemplate : IGeneratedCorpusTemplate
{
    private static readonly ImmutableDictionary<string, string> Branches =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["if"] = "if (((current + input) & 1) == 0) { __TRANSFER__ }",
            ["short-circuit"] =
                "if (current >= 0 && input != int.MinValue && ((current + input) & 1) == 0) { __TRANSFER__ }",
            ["conditional"] =
                "var takeTransfer = ((current + input) & 1) == 0 ? true : false; if (takeTransfer) { __TRANSFER__ }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Transfers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["break"] = "total += 17; break;",
            ["continue"] = "total += 19; continue;",
            ["return"] = "return Finish(total + 23);",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Loops =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["while"] =
                "var index = 0; while (index < count) { var current = index++; __BODY__ total += current + 3; }",
            ["do"] =
                "var index = 0; do { var current = index++; __BODY__ total += current + 5; } while (index < count);",
            ["for"] =
                "for (var index = 0; index < count;) { var current = index++; __BODY__ total += current + 7; }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public string Family => "ControlFlow";

    public int Seed => 0x13572468;

    public ImmutableArray<CorpusDimension> Dimensions =>
    [
        new("branch", ["if", "short-circuit", "conditional"]),
        new("loop", ["while", "do", "for"]),
        new("transfer", ["break", "continue", "return"]),
    ];

    public ImmutableArray<ImmutableDictionary<string, string>> TargetedCases =>
    [
        Values(("branch", "short-circuit"), ("loop", "do"), ("transfer", "continue")),
    ];

    public CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values)
    {
        var branch = Branches[values["branch"]]
            .Replace("__TRANSFER__", Transfers[values["transfer"]], StringComparison.Ordinal);
        var loop = Loops[values["loop"]]
            .Replace("__BODY__", branch, StringComparison.Ordinal);
        var source = $$"""
            namespace NetWasm.Correctness.Generated.{{caseName}};

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    var total = input;
                    var count = (input & 3) + 1;
                    {{loop}}
                    return Finish(total + 29);
                }

                private static int Finish(int value)
                {
                    _trace = unchecked(_trace * 31 + value);
                    return value;
                }

                public static int Trace() => _trace;
            }
            """;
        return new(caseName, $"NetWasm.Correctness.Generated.{caseName}", source,
            [-3, 0, 1, 4]);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
