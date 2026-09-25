using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ValueCallCorpusTemplate : IGeneratedCorpusTemplate
{
    private static readonly ImmutableDictionary<string, ValueShape> Values =
        new Dictionary<string, ValueShape>(StringComparer.Ordinal)
        {
            ["int"] = new("int", "input + 1", "value"),
            ["long"] = new("long", "(long)input * 101 + 2", "(int)(value % 1009)"),
            ["struct"] = new(
                "Payload",
                "new Payload(input + 3, input == 0 ? \"zero\" : \"value\")",
                "value.Number + value.Tag.Length"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Storage =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["local"] = "var stored = value;",
            ["field"] = "_field = value; var stored = _field;",
            ["array"] =
                "var storage = new __TYPE__[2]; storage[1] = value; var stored = storage[1];",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Calls =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["direct"] =
                "private static int Invoke(__TYPE__ value) => __READ__;",
            ["virtual"] =
                "private abstract class Reader { public abstract int Read(__TYPE__ value); } private sealed class DerivedReader : Reader { public override int Read(__TYPE__ value) => __READ__; } private static int Invoke(__TYPE__ value) { Reader reader = new DerivedReader(); return reader.Read(value); }",
            ["delegate"] =
                "private delegate int Reader(__TYPE__ value); private static int Read(__TYPE__ value) => __READ__; private static int Invoke(__TYPE__ value) { Reader reader = Read; return reader(value); }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public string Family => "ValueCall";

    public int Seed => 0x31415926;

    public ImmutableArray<CorpusDimension> Dimensions =>
    [
        new("value", ["int", "long", "struct"]),
        new("storage", ["local", "field", "array"]),
        new("call", ["direct", "virtual", "delegate"]),
    ];

    public ImmutableArray<ImmutableDictionary<string, string>> TargetedCases =>
    [
        ValuesFor(("value", "struct"), ("storage", "array"), ("call", "delegate")),
        ValuesFor(("value", "long"), ("storage", "field"), ("call", "virtual")),
    ];

    public CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values)
    {
        var value = Values[values["value"]];
        var storage = ReplaceType(Storage[values["storage"]], value.Type);
        var call = ReplaceType(Calls[values["call"]], value.Type)
            .Replace("__READ__", value.Read, StringComparison.Ordinal);
        var field = values["storage"] == "field"
            ? $"private static {value.Type} _field;"
            : string.Empty;
        var source = $$"""
            namespace NetWasm.Correctness.Generated.{{caseName}};

            public readonly struct Payload
            {
                public Payload(int number, string tag)
                {
                    Number = number;
                    Tag = tag;
                }

                public int Number { get; }

                public string Tag { get; }
            }

            public static class EntryPoint
            {
                {{field}}
                private static int _trace;

                public static int Run(int input)
                {
                    var value = {{value.Create}};
                    {{storage}}
                    var result = Invoke(stored);
                    _trace = unchecked(result * 31 + input);
                    return result;
                }

                {{call}}

                public static int Trace() => _trace;
            }
            """;
        return new(caseName, $"NetWasm.Correctness.Generated.{caseName}", source,
            [-7, 0, 1, 41]);
    }

    private static string ReplaceType(string source, string type) =>
        source.Replace("__TYPE__", type, StringComparison.Ordinal);

    private static ImmutableDictionary<string, string> ValuesFor(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private sealed record ValueShape(string Type, string Create, string Read);
}
