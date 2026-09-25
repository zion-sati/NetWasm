using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ExceptionTransferCorpusTemplate : IGeneratedCorpusTemplate
{
    private static readonly ImmutableDictionary<string, string> Transfers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["break"] = "if (((current + input) & 1) == 0) { total += 31; break; }",
            ["continue"] = "if (((current + input) & 1) == 0) { total += 37; continue; }",
            ["return"] =
                "if (((current + input) & 1) == 0) { return Finish(ShapeValue(total + 41)); }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Regions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["finally"] =
                "try { total += current + 3; __TRANSFER__ } finally { _trace = unchecked(_trace * 17 + current + 1); }",
            ["catch"] =
                "try { if (((current + input) & 3) == 0) throw new Marker(current); total += current + 5; __TRANSFER__ } catch (Marker error) { total += error.Code + 43; _trace += 7; }",
            ["filter"] =
                "try { if (((current + input) & 3) == 0) throw new Marker(current); total += current + 11; __TRANSFER__ } catch (Marker error) when ((error.Code & 1) == 0) { total += 47; _trace += 13; } catch (Marker error) { total += error.Code + 53; _trace += 19; }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Shapes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["scalar"] = "private static int ShapeValue(int value) => value + 1;",
            ["struct"] =
                "private readonly struct Result { public Result(int value) { Value = value; } public int Value { get; } } private static Result Shape(int value) => new(value + 2); private static int ShapeValue(int value) => Shape(value).Value;",
            ["reference"] =
                "private sealed class Result { public Result(int value) { Value = value; } public int Value { get; } } private static Result Shape(int value) => new(value + 3); private static int ShapeValue(int value) => Shape(value).Value;",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public string Family => "ExceptionTransfer";

    public int Seed => 0x24681357;

    public ImmutableArray<CorpusDimension> Dimensions =>
    [
        new("region", ["finally", "catch", "filter"]),
        new("transfer", ["break", "continue", "return"]),
        new("return", ["scalar", "struct", "reference"]),
    ];

    public ImmutableArray<ImmutableDictionary<string, string>> TargetedCases =>
    [
        Values(("region", "finally"), ("transfer", "return"), ("return", "struct")),
        Values(("region", "filter"), ("transfer", "continue"), ("return", "reference")),
    ];

    public CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values)
    {
        var region = Regions[values["region"]]
            .Replace("__TRANSFER__", Transfers[values["transfer"]], StringComparison.Ordinal);
        var source = $$"""
            using System;

            namespace NetWasm.Correctness.Generated.{{caseName}};

            public sealed class Marker : Exception
            {
                public Marker(int code) => Code = code;

                public int Code { get; }
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    var total = input;
                    for (var current = 0; current < 4; current++)
                    {
                        {{region}}
                    }
                    return Finish(ShapeValue(total + 59));
                }

                {{Shapes[values["return"]]}}

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
