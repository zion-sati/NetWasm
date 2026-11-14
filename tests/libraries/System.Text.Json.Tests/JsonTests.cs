using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.Json.Tests;

public enum DocumentState
{
    Unknown,
    Ready,
    Complete,
}

[JsonConverter(typeof(MarkerConverter))]
public readonly record struct Marker(int Value);

public sealed record Pair<T>(T First, T Second);

public sealed record Document(
    string Name,
    int? Count,
    DocumentState State,
    List<int> Values,
    Dictionary<string, int?> Metrics,
    Pair<Marker> Pair);

public sealed record ScalarDocument(
    DateTime When,
    DateTimeOffset Offset,
    Guid Id,
    decimal Amount,
    long Count,
    double Ratio);

public sealed record UnregisteredPayload(int Value);

public sealed record NamedDocument(string DisplayName, int Value);

public sealed class ExtensionEnvelope
{
    [JsonPropertyOrder(-1)]
    public string Head { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string Tail { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
}

public sealed class NodeDocument
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public JsonArray Items { get; set; } = [];
}

public sealed class NodePayloadEnvelope
{
    public object? Payload { get; set; }
}

#if NETWASM
public sealed class NodeExtensionEnvelope
{
    public string Name { get; set; } = string.Empty;

    [JsonExtensionData]
    public JsonObject ExtensionData { get; set; } = [];
}
#endif

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
[JsonSerializable(typeof(Document))]
[JsonSerializable(typeof(ScalarDocument))]
[JsonSerializable(typeof(Marker))]
[JsonSerializable(typeof(Pair<Marker>))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(ExtensionEnvelope))]
public partial class JsonTestContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NamedDocument))]
public partial class NamedJsonTestContext : JsonSerializerContext;

public sealed record StringStateDocument(
    [property: JsonConverter(typeof(JsonStringEnumConverter<DocumentState>))] DocumentState State);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StringStateDocument))]
public partial class StringEnumJsonTestContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode)]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonValue))]
[JsonSerializable(typeof(NodeDocument))]
[JsonSerializable(typeof(NodePayloadEnvelope))]
#if NETWASM
[JsonSerializable(typeof(NodeExtensionEnvelope))]
#endif
[JsonSerializable(typeof(object))]
public partial class JsonNodeTestContext : JsonSerializerContext;

public sealed class JsonTests
{
    [Test]
    public async Task SourceGeneratedContextPublishesTypedMetadata()
    {
        var documentInfo = JsonTestContext.Default.Document;
        var nullableInfo = JsonTestContext.Default.NullableInt32;
        var markerInfo = JsonTestContext.Default.Marker;

        await Assert.That(documentInfo.Converter.Type).IsEqualTo(typeof(Document));
        await Assert.That(nullableInfo.Converter.Type).IsEqualTo(typeof(int?));
        await Assert.That(nullableInfo.Converter.CanConvert(typeof(int?))).IsTrue();
        await Assert.That(markerInfo.Converter.Type).IsEqualTo(typeof(Marker));
    }

    [Test]
    public async Task SourceGeneratedOptionsResolveAndCacheRegisteredMetadata()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTestContext.Default,
        };
        options.MakeReadOnly();

        var first = options.GetTypeInfo(typeof(Document));
        var second = options.GetTypeInfo(typeof(Document));
        var found = options.TryGetTypeInfo(typeof(Document), out var foundInfo);
        var missing = options.TryGetTypeInfo(typeof(UnregisteredPayload), out var missingInfo);
        var markerConverter = options.GetConverter(typeof(Marker));

        await Assert.That(first.Type).IsEqualTo(typeof(Document));
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(found).IsTrue();
        await Assert.That(ReferenceEquals(first, foundInfo)).IsTrue();
        await Assert.That(markerConverter.Type).IsEqualTo(typeof(Marker));
        await Assert.That(missing).IsFalse();
        await Assert.That(missingInfo).IsNull();
    }

    [Test]
    public async Task GeneratedDocumentRoundTripsNullableEnumCollectionsAndNestedGeneric()
    {
        var expected = new Document(
            "generated",
            42,
            DocumentState.Ready,
            [2, 4, 6],
            new Dictionary<string, int?>
            {
                ["present"] = 7,
                ["missing"] = null,
            },
            new Pair<Marker>(new(3), new(9)));

        var json = JsonSerializer.Serialize(expected, JsonTestContext.Default.Document);
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.Document)
            ?? throw new InvalidOperationException("generated document deserialized to null");

        await Assert.That(actual.Name).IsEqualTo(expected.Name);
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(actual.State).IsEqualTo(expected.State);
        await Assert.That(actual.Values.Count).IsEqualTo(3);
        await Assert.That(actual.Values[0]).IsEqualTo(2);
        await Assert.That(actual.Values[2]).IsEqualTo(6);
        await Assert.That(actual.Metrics["present"]).IsEqualTo(7);
        await Assert.That(actual.Metrics["missing"]).IsNull();
        await Assert.That(actual.Pair.First.Value).IsEqualTo(3);
        await Assert.That(actual.Pair.Second.Value).IsEqualTo(9);
    }

    [Test]
    public async Task GeneratedNullableAndEnumValuesRetainTheirWireShape()
    {
        var nullDocument = new Document(
            "null",
            null,
            DocumentState.Unknown,
            [],
            new Dictionary<string, int?>(),
            new Pair<Marker>(new(0), new(1)));
        var populatedDocument = nullDocument with
        {
            Count = 0,
            State = DocumentState.Complete,
        };

        var nullJson = JsonSerializer.Serialize(nullDocument, JsonTestContext.Default.Document);
        var populatedJson = JsonSerializer.Serialize(
            populatedDocument,
            JsonTestContext.Default.Document);
        var nullActual = JsonSerializer.Deserialize(nullJson, JsonTestContext.Default.Document)
            ?? throw new InvalidOperationException("null document deserialized to null");
        var populatedActual = JsonSerializer.Deserialize(
            populatedJson,
            JsonTestContext.Default.Document)
            ?? throw new InvalidOperationException("populated document deserialized to null");

        await Assert.That(nullJson.Contains("\"Count\":null", StringComparison.Ordinal)).IsTrue();
        await Assert.That(populatedJson.Contains("\"Count\":0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(nullActual.Count).IsNull();
        await Assert.That(nullActual.State).IsEqualTo(DocumentState.Unknown);
        await Assert.That(populatedActual.Count).IsEqualTo(0);
        await Assert.That(populatedActual.State).IsEqualTo(DocumentState.Complete);
    }

    [Test]
    public async Task GeneratedMetadataUsesTheCustomValueConverter()
    {
        var valueJson = JsonSerializer.Serialize(new Marker(23), JsonTestContext.Default.Marker);
        var pairJson = JsonSerializer.Serialize(
            new Pair<Marker>(new(4), new(8)),
            JsonTestContext.Default.PairMarker);
        var value = JsonSerializer.Deserialize(valueJson, JsonTestContext.Default.Marker);
        var pair = JsonSerializer.Deserialize(pairJson, JsonTestContext.Default.PairMarker)
            ?? throw new InvalidOperationException("generated pair deserialized to null");

        await Assert.That(value.Value).IsEqualTo(23);
        await Assert.That(valueJson).IsEqualTo("123");
        await Assert.That(pairJson).IsEqualTo("{\"First\":104,\"Second\":108}");
        await Assert.That(pair.First.Value).IsEqualTo(4);
        await Assert.That(pair.Second.Value).IsEqualTo(8);
    }

    [Test]
    public async Task GeneratedMetadataPreservesInvariantScalarBoundaries()
    {
        var expected = new ScalarDocument(
            new DateTime(2020, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc).AddTicks(9012),
            new DateTimeOffset(2020, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(2)).AddTicks(9012),
            new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            12.5000m,
            long.MaxValue,
            -1250.5d);

        var json = JsonSerializer.Serialize(expected, JsonTestContext.Default.ScalarDocument);
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.ScalarDocument)
            ?? throw new InvalidOperationException("generated scalar document deserialized to null");

        await Assert.That(actual.When).IsEqualTo(expected.When);
        await Assert.That(actual.Offset).IsEqualTo(expected.Offset);
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Amount).IsEqualTo(expected.Amount);
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(actual.Ratio).IsEqualTo(expected.Ratio);
    }

    [Test]
    public async Task GeneratedStringValuesEscapeUnicodeAndRoundTrip()
    {
        var expected = new Document(
            "A\"mé😀",
            1,
            DocumentState.Ready,
            [1],
            new Dictionary<string, int?>
            {
                ["A\"mé"] = 1,
            },
            new Pair<Marker>(new(1), new(2)));

        var json = JsonSerializer.Serialize(expected, JsonTestContext.Default.Document);
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.Document)
            ?? throw new InvalidOperationException("escaped document deserialized to null");

        await Assert.That(json.Contains("\\u0022", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\\u00E9", StringComparison.Ordinal)).IsTrue();
        await Assert.That(actual.Name).IsEqualTo(expected.Name);
        await Assert.That(actual.Metrics.ContainsKey("A\"mé")).IsTrue();
    }

    [Test]
    public async Task GeneratedDeserializationRejectsMalformedPayloads()
    {
        var malformed = "{\"Name\":\"broken\",\"Count\":not-a-number}";
        var wrongShape = "[1,2,3]";
        var malformedRejected = ThrowsJson(
            () => JsonSerializer.Deserialize(malformed, JsonTestContext.Default.Document));
        var wrongShapeRejected = ThrowsJson(
            () => JsonSerializer.Deserialize(wrongShape, JsonTestContext.Default.Document));

        await Assert.That(malformedRejected).IsTrue();
        await Assert.That(wrongShapeRejected).IsTrue();
    }

    [Test]
    public async Task ExplicitResolverRejectsTypesWithoutGeneratedMetadata()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new EmptyTypeInfoResolver(),
        };

        var rejected = ThrowsNotSupported(
            () => JsonSerializer.Serialize(new UnregisteredPayload(17), options));

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task GeneratedExtensionDataAndPropertyOrderPreserveContract()
    {
        const string source = "{\"Tail\":\"end\",\"extra\":17,\"Head\":\"start\"}";
        var envelope = JsonSerializer.Deserialize(source, JsonTestContext.Default.ExtensionEnvelope)
            ?? throw new InvalidOperationException("extension envelope deserialized to null");
        var json = JsonSerializer.Serialize(envelope, JsonTestContext.Default.ExtensionEnvelope);

        await Assert.That(envelope.Head).IsEqualTo("start");
        await Assert.That(envelope.Tail).IsEqualTo("end");
        await Assert.That(envelope.ExtensionData.ContainsKey("extra")).IsTrue();
        await Assert.That(envelope.ExtensionData["extra"].GetInt32()).IsEqualTo(17);
        await Assert.That(
                json.IndexOf("\"Head\"", StringComparison.Ordinal)
                < json.IndexOf("\"Tail\"", StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(json.Contains("\"extra\":17", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task JsonDocumentNavigationPreservesPrimitiveKindsAndMissingProperties()
    {
        using var document = JsonDocument.Parse(
            " { \"name\":\"value\", \"items\":[1,true,null] } ");
        var root = document.RootElement;
        var items = root.GetProperty("items");
        var enumerator = items.EnumerateArray();

        await Assert.That(root.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("value");
        await Assert.That(items.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(enumerator.MoveNext()).IsTrue();
        await Assert.That(enumerator.Current.GetInt32()).IsEqualTo(1);
        await Assert.That(enumerator.MoveNext()).IsTrue();
        await Assert.That(enumerator.Current.ValueKind).IsEqualTo(JsonValueKind.True);
        await Assert.That(enumerator.MoveNext()).IsTrue();
        await Assert.That(enumerator.Current.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(enumerator.MoveNext()).IsFalse();
        await Assert.That(root.TryGetProperty("missing", out _)).IsFalse();
    }

    // A writer over a Stream buffers until Flush, writes on Dispose and DisposeAsync, and Reset moves it to another
    // stream; a read-only stream is rejected (AWSSDK.Core writes its request metrics through this constructor).
    [Test]
    public async Task Utf8JsonWriterWritesToAStream()
    {
        var first = new MemoryStream();
        using (var writer = new Utf8JsonWriter(first))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", 7);
            writer.WriteEndObject();
            await Assert.That(first.Length).IsEqualTo(0);
            writer.Flush();
            await Assert.That(global::System.Text.Encoding.UTF8.GetString(first.ToArray())).IsEqualTo("{\"count\":7}");

            var second = new MemoryStream();
            writer.Reset(second);
            writer.WriteStringValue("next");
        }
        var asynchronous = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(asynchronous, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            writer.WriteBooleanValue(true);
            writer.WriteEndArray();
            await writer.FlushAsync();
        }
        await Assert.That(global::System.Text.Encoding.UTF8.GetString(asynchronous.ToArray())).IsEqualTo("[true]");
        var rejected = false;
        try
        {
            _ = new Utf8JsonWriter(new MemoryStream([], writable: false));
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task Utf8JsonReaderAndWriterPreservePrimitiveTokenContracts()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("name", "value");
            writer.WriteNumber("count", 7);
            writer.WriteBoolean("ready", true);
            writer.WriteNull("missing");
            writer.WriteEndObject();
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        var tokens = new List<JsonTokenType>();
        while (reader.Read())
        {
            tokens.Add(reader.TokenType);
        }
        var bytesConsumed = reader.BytesConsumed;

        await Assert.That(tokens.Count).IsEqualTo(10);
        await Assert.That(tokens[0]).IsEqualTo(JsonTokenType.StartObject);
        await Assert.That(tokens[1]).IsEqualTo(JsonTokenType.PropertyName);
        await Assert.That(tokens[2]).IsEqualTo(JsonTokenType.String);
        await Assert.That(tokens[3]).IsEqualTo(JsonTokenType.PropertyName);
        await Assert.That(tokens[4]).IsEqualTo(JsonTokenType.Number);
        await Assert.That(tokens[5]).IsEqualTo(JsonTokenType.PropertyName);
        await Assert.That(tokens[6]).IsEqualTo(JsonTokenType.True);
        await Assert.That(tokens[7]).IsEqualTo(JsonTokenType.PropertyName);
        await Assert.That(tokens[8]).IsEqualTo(JsonTokenType.Null);
        await Assert.That(tokens[9]).IsEqualTo(JsonTokenType.EndObject);
        await Assert.That(bytesConsumed).IsEqualTo(buffer.WrittenCount);
    }

    [Test]
    public async Task GeneratedNamingPolicyPreservesGeneratedPropertyContract()
    {
        var expected = new NamedDocument("portable", 12);
        var json = JsonSerializer.Serialize(expected, NamedJsonTestContext.Default.NamedDocument);
        var actual = JsonSerializer.Deserialize(json, NamedJsonTestContext.Default.NamedDocument)
            ?? throw new InvalidOperationException("named document deserialized to null");

        await Assert.That(json).IsEqualTo("{\"displayName\":\"portable\",\"value\":12}");
        await Assert.That(actual.DisplayName).IsEqualTo(expected.DisplayName);
        await Assert.That(actual.Value).IsEqualTo(expected.Value);
    }

    [Test]
    public async Task JsonNodeParsesMutatesIndexesAndPreservesOwnership()
    {
        JsonObject root = JsonNode.Parse("{\"name\":\"before\",\"items\":[1,null]}")!.AsObject();
        JsonArray items = root["items"]!.AsArray();

        root["name"] = "after";
        items[0] = 7;
        items.Insert(1, "middle");
        items.Add(9);

        await Assert.That(root["name"]!.GetValue<string>()).IsEqualTo("after");
        await Assert.That(items.Count).IsEqualTo(4);
        await Assert.That(items[0]!.GetValue<int>()).IsEqualTo(7);
        await Assert.That(items[1]!.GetValue<string>()).IsEqualTo("middle");
        await Assert.That(items[2]).IsNull();
        await Assert.That(items[3]!.GetValue<int>()).IsEqualTo(9);
        await Assert.That(items[1]!.Parent).IsEqualTo(items);
        await Assert.That(items[1]!.GetElementIndex()).IsEqualTo(1);
        await Assert.That(root["name"]!.GetPropertyName()).IsEqualTo("name");
        await Assert.That(root["name"]!.GetPath()).IsEqualTo("$.name");
        await Assert.That(items[1]!.GetPath()).IsEqualTo("$.items[1]");
        await Assert.That(root.ToJsonString()).IsEqualTo("{\"name\":\"after\",\"items\":[7,\"middle\",null,9]}");
    }

    [Test]
    public async Task JsonNodeSupportsPrimitivesNullAndDeepClone()
    {
        JsonNode? nullNode = JsonNode.Parse("null");
        JsonNode booleanNode = JsonNode.Parse("true")!;
        JsonNode stringNode = JsonNode.Parse("\"text\"")!;
        JsonValue integerNode = JsonValue.Create(42);
        JsonValue? nullableNode = JsonValue.Create((int?)null);
        object boxedInteger = 43;
        var boxedArray = new JsonArray();
        boxedArray.Add(boxedInteger);

        await Assert.That(nullNode).IsNull();
        await Assert.That(booleanNode.GetValue<bool>()).IsTrue();
        await Assert.That(stringNode.GetValue<string>()).IsEqualTo("text");
        await Assert.That(integerNode.GetValue<int>()).IsEqualTo(42);
        await Assert.That(nullableNode).IsNull();
        await Assert.That(boxedArray[0]!.GetValue<int>()).IsEqualTo(43);

        JsonObject original = JsonNode.Parse("{\"a\":[1,{\"b\":true}]}")!.AsObject();
        JsonNode clone = original.DeepClone();

        await Assert.That(JsonNode.DeepEquals(original, clone)).IsTrue();
        clone.AsObject()["a"]!.AsArray()[1]!["b"] = false;
        await Assert.That(JsonNode.DeepEquals(original, clone)).IsFalse();
    }

    [Test]
    public async Task JsonNodeSerializesThroughGeneratedMetadata()
    {
        var value = new NodeDocument
        {
            Name = "generated",
            Count = 2,
            Items = new JsonArray(1, "two", null),
        };

        JsonNode node = JsonSerializer.SerializeToNode(value, JsonNodeTestContext.Default.NodeDocument)!
            .AsObject();
        NodeDocument actual = JsonSerializer.Deserialize(node, JsonNodeTestContext.Default.NodeDocument)
            ?? throw new InvalidOperationException("node document deserialized to null");

        await Assert.That(node.ToJsonString()).IsEqualTo("{\"Name\":\"generated\",\"Count\":2,\"Items\":[1,\"two\",null]}");
        await Assert.That(actual.Name).IsEqualTo(value.Name);
        await Assert.That(actual.Count).IsEqualTo(value.Count);
        await Assert.That(actual.Items.Count).IsEqualTo(3);
        await Assert.That(actual.Items[1]!.GetValue<string>()).IsEqualTo("two");

#if NETWASM
        // Exercise the package's JsonObject extension-data lane in the target
        // where this port is the subject implementation.
        NodeExtensionEnvelope extension = JsonSerializer.Deserialize(
                "{\"Name\":\"head\",\"count\":2,\"items\":[true,null]}",
                JsonNodeTestContext.Default.NodeExtensionEnvelope)
            ?? throw new InvalidOperationException("node extension envelope deserialized to null");
        string extensionJson = JsonSerializer.Serialize(extension, JsonNodeTestContext.Default.NodeExtensionEnvelope);
        await Assert.That(extensionJson).IsEqualTo("{\"Name\":\"head\",\"count\":2,\"items\":[true,null]}");
#endif
    }

    [Test]
    public async Task JsonUnknownTypeHandlingJsonNodeMaterializesObjectValues()
    {
        NodePayloadEnvelope value = JsonSerializer.Deserialize(
                "{\"Payload\":{\"answer\":42,\"items\":[true,null]}}",
                JsonNodeTestContext.Default.NodePayloadEnvelope)
            ?? throw new InvalidOperationException("node payload deserialized to null");

        await Assert.That(value.Payload is JsonObject).IsTrue();
        JsonObject payload = (JsonObject)value.Payload!;
        await Assert.That(payload["answer"]!.GetValue<int>()).IsEqualTo(42);
        await Assert.That(payload["items"]!.AsArray()[0]!.GetValue<bool>()).IsTrue();
        await Assert.That(payload["items"]!.AsArray()[1]).IsNull();
    }

    [Test]
    public async Task JsonNodeRejectsDuplicateOwnershipAndCycles()
    {
        JsonObject child = new();
        JsonArray first = new(child);
        JsonArray second = new();

        await Assert.That(ThrowsInvalidOperation(() => second.Add(child))).IsTrue();
        await Assert.That(ThrowsInvalidOperation(() => first.Add(first))).IsTrue();

        JsonObject duplicate = new() { ["value"] = 1 };
        await Assert.That(ThrowsArgument(() => duplicate.Add("value", 2))).IsTrue();

        first.RemoveAt(0);
        await Assert.That(child.Parent).IsNull();
        second.Add(child);
        await Assert.That(child.Parent).IsEqualTo(second);
    }

    private static bool ThrowsJson(Func<object?> operation)
    {
        try
        {
            _ = operation();
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool ThrowsNotSupported(Func<object?> operation)
    {
        try
        {
            _ = operation();
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private static bool ThrowsInvalidOperation(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool ThrowsArgument(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Test]
    public async Task GenericStringEnumConverterWritesAndReadsMemberNames()
    {
        var json = JsonSerializer.Serialize(new StringStateDocument(DocumentState.Complete),
            StringEnumJsonTestContext.Default.StringStateDocument);
        var named = JsonSerializer.Deserialize("{\"State\":\"Ready\"}", StringEnumJsonTestContext.Default.StringStateDocument);
        var numbered = JsonSerializer.Deserialize("{\"State\":2}", StringEnumJsonTestContext.Default.StringStateDocument);

        await Assert.That(json).IsEqualTo("{\"State\":\"Complete\"}");
        await Assert.That(named!.State).IsEqualTo(DocumentState.Ready);
        await Assert.That(numbered!.State).IsEqualTo(DocumentState.Complete);
        await Assert.That(new JsonStringEnumConverter<DocumentState>().CanConvert(typeof(DocumentState))).IsTrue();
        await Assert.That(new JsonStringEnumConverter<DocumentState>().CanConvert(typeof(int))).IsFalse();
    }

    [Test]
    public async Task NonGenericStringEnumConverterRecognizesEnumsButNeedsReflectionToCreateOne()
    {
        var converter = new JsonStringEnumConverter();

        await Assert.That(converter.CanConvert(typeof(DocumentState))).IsTrue();
        await Assert.That(converter.CanConvert(typeof(int))).IsFalse();
#if NETWASM
        // Creating a converter for an enum named at run time constructs EnumConverter<T> by reflection.
        await Assert.That(ThrowsNotSupported(() => converter.CreateConverter(typeof(DocumentState), new JsonSerializerOptions())))
            .IsTrue();
#else
        await Assert.That(converter.CreateConverter(typeof(DocumentState), new JsonSerializerOptions()).Type)
            .IsEqualTo(typeof(DocumentState));
#endif
    }

    private sealed class EmptyTypeInfoResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }
}
