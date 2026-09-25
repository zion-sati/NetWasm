using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NetWasm.Tests.StjSourceGeneration.Fixture;

public enum FixtureKind
{
    Unknown,
    Alpha,
    Beta,
}

[JsonConverter(typeof(MarkerConverter))]
public readonly record struct Marker(int Value);

public sealed record Envelope(
    string Name,
    int? Count,
    FixtureKind Kind,
    List<int> Values,
    Dictionary<string, int?> Metrics,
    Nested.Pair<Marker> Pair);

public static class Nested
{
    public sealed record Pair<T>(T First, T Second);
}

public sealed class MarkerConverter : JsonConverter<Marker>
{
    public override Marker Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => new(reader.GetInt32() - 100);

    public override void Write(
        Utf8JsonWriter writer,
        Marker value,
        JsonSerializerOptions options) => writer.WriteNumberValue(value.Value + 100);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(Nested.Pair<Marker>))]
public partial class FixtureJsonContext : JsonSerializerContext;

public static class EntryPoint
{
    public static int Run(int input)
    {
        var name = "fixture-" + input;
        int? count = input % 2 == 0 ? input : null;
        var kind = (input % 3) switch
        {
            0 => FixtureKind.Alpha,
            1 => FixtureKind.Beta,
            _ => FixtureKind.Unknown,
        };
        var values = new List<int> { input, input + 1 };
        var metrics = new Dictionary<string, int?>
        {
            ["input"] = input,
            ["nullable"] = input % 2 == 0 ? null : input + 10,
        };
        var pair = new Nested.Pair<Marker>(new(input + 4), new(input + 8));
        var expected = new Envelope(name, count, kind, values, metrics, pair);

        Serialize(name, FixtureJsonContext.Default.String);
        Serialize(input, FixtureJsonContext.Default.Int32);
        var nullableTypeInfo = FixtureJsonContext.Default.NullableInt32;
        if (nullableTypeInfo.Converter.Type != typeof(int?))
        {
            throw new InvalidOperationException(
                "source-generated nullable converter has an incompatible type");
        }
        if (!nullableTypeInfo.Converter.CanConvert(typeof(int?)))
        {
            throw new InvalidOperationException(
                "source-generated nullable converter rejected its declared type");
        }
        Serialize(count, nullableTypeInfo);
        Serialize(kind, FixtureJsonContext.Default.FixtureKind);
        Serialize(values, FixtureJsonContext.Default.ListInt32);
        Serialize(metrics, FixtureJsonContext.Default.DictionaryStringNullableInt32);
        Serialize(pair.First, FixtureJsonContext.Default.Marker);
        Serialize(pair, FixtureJsonContext.Default.PairMarker);

        var envelopeTypeInfo = FixtureJsonContext.Default.Envelope;
        var json = JsonSerializer.Serialize(expected, envelopeTypeInfo);

        var actual = JsonSerializer.Deserialize(json, envelopeTypeInfo)
            ?? throw new InvalidOperationException(
                "source-generated deserialization returned null");

        var valuesMatch = actual.Values.Count == expected.Values.Count;
        if (valuesMatch)
        {
            for (var index = 0; index < actual.Values.Count; index++)
            {
                valuesMatch &= actual.Values[index] == expected.Values[index];
            }
        }

        if (actual.Name != expected.Name ||
            actual.Count != expected.Count ||
            actual.Kind != expected.Kind ||
            !valuesMatch ||
            actual.Metrics.Count != expected.Metrics.Count ||
            actual.Metrics["input"] != expected.Metrics["input"] ||
            actual.Metrics["nullable"] != expected.Metrics["nullable"] ||
            actual.Pair.First != expected.Pair.First ||
            actual.Pair.Second != expected.Pair.Second ||
            actual.Pair.First.Value != input + 4 ||
            actual.Pair.Second.Value != input + 8 ||
            actual.Metrics["input"] != input ||
            !actual.Metrics.ContainsKey("nullable") ||
            actual.Name != "fixture-" + input)
        {
            return -1;
        }

        return 1000 + input;
    }

    private static void Serialize<T>(T value, JsonTypeInfo<T> typeInfo) =>
        _ = JsonSerializer.Serialize(value, typeInfo);
}
