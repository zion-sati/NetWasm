using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GenericDispatchCorpusTemplate : IGeneratedCorpusTemplate
{
    private static readonly ImmutableDictionary<string, GenericShape> Specializations =
        new Dictionary<string, GenericShape>(StringComparer.Ordinal)
        {
            ["reference"] = new("Node", "new Node(input + 1)", "recovered.Number"),
            ["value"] = new("Pair", "new Pair(input + 2)", "recovered.Number"),
            ["reference-value"] = new(
                "ReferencePair",
                "new ReferencePair(input + 3, input == 0 ? \"zero\" : \"generic\")",
                "recovered.Number + recovered.Text.Length"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Boxing =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["none"] = "var recovered = Identity(value);",
            ["object"] = "object boxed = Identity(value); var recovered = (__TYPE__)boxed;",
            ["interface"] = "IRead boxed = Identity(value); var recovered = (__TYPE__)boxed;",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Dispatch =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["direct"] = "var result = __DIRECT__;",
            ["interface"] = "IRead reader = recovered; var result = reader.Read();",
            ["virtual"] =
                "ReaderBase reader = new Holder<__TYPE__>(recovered); var result = reader.Read();",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public string Family => "GenericDispatch";

    public int Seed => 0x10293847;

    public ImmutableArray<CorpusDimension> Dimensions =>
    [
        new("specialization", ["reference", "value", "reference-value"]),
        new("dispatch", ["direct", "interface", "virtual"]),
        new("boxing", ["none", "object", "interface"]),
    ];

    public ImmutableArray<ImmutableDictionary<string, string>> TargetedCases =>
    [
        Values(("specialization", "reference-value"), ("dispatch", "interface"),
            ("boxing", "object")),
        Values(("specialization", "value"), ("dispatch", "virtual"),
            ("boxing", "interface")),
    ];

    public CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values)
    {
        var shape = Specializations[values["specialization"]];
        var boxing = Boxing[values["boxing"]]
            .Replace("__TYPE__", shape.Type, StringComparison.Ordinal);
        var dispatch = Dispatch[values["dispatch"]]
            .Replace("__TYPE__", shape.Type, StringComparison.Ordinal)
            .Replace("__DIRECT__", shape.DirectRead, StringComparison.Ordinal);
        var source = $$"""
            namespace NetWasm.Correctness.Generated.{{caseName}};

            public interface IRead
            {
                int Read();
            }

            public sealed class Node(int number) : IRead
            {
                public int Number { get; } = number;

                public int Read() => Number;
            }

            public readonly struct Pair(int number) : IRead
            {
                public int Number { get; } = number;

                public int Read() => Number;
            }

            public readonly struct ReferencePair(int number, string text) : IRead
            {
                public int Number { get; } = number;

                public string Text { get; } = text;

                public int Read() => Number + Text.Length;
            }

            public abstract class ReaderBase
            {
                public abstract int Read();
            }

            public sealed class Holder<T>(T value) : ReaderBase where T : IRead
            {
                private T Value { get; } = value;

                public override int Read() => Value.Read();
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    var value = {{shape.Create}};
                    {{boxing}}
                    {{dispatch}}
                    _trace = unchecked(result * 31 + input);
                    return result;
                }

                private static T Identity<T>(T value) => value;

                public static int Trace() => _trace;
            }
            """;
        return new(caseName, $"NetWasm.Correctness.Generated.{caseName}", source,
            [-5, 0, 1, 41]);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private sealed record GenericShape(string Type, string Create, string DirectRead);
}
