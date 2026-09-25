using System.Buffers;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Tests.StjGeneratedShapeClosure.Fixture;

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

public sealed record NamedDocument(string DisplayName, int Value);

public sealed record UnregisteredPayload(int Value);

public sealed class ExtensionEnvelope
{
    [JsonPropertyOrder(-1)]
    public string Head { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string Tail { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = [];
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

public sealed record GeneratedMixedChild(string Value);

public sealed record GeneratedMixedModel(
    string Text,
    int? Number,
    GeneratedMixedChild First,
    List<string> Items,
    Dictionary<string, int> Map,
    GeneratedMixedChild Second);

[JsonSerializable(typeof(GeneratedMixedModel))]
public sealed partial class GeneratedMixedContext : JsonSerializerContext;

public static class AdditionalRootCatalog
{
    internal static async Task<int> RootMetadataAccessAsync()
    {
        await Task.CompletedTask;
        return RootMetadataAccess();
    }

    internal static async Task<int> RootDocumentRoundTripAsync()
    {
        await Task.CompletedTask;
        return RootDocumentRoundTrip();
    }

    internal static async Task<int> RootNullableAndEnumRoundTripAsync()
    {
        await Task.CompletedTask;
        return RootNullableAndEnumRoundTrip();
    }

    internal static async Task<int> RootCustomConverterRoundTripAsync()
    {
        await Task.CompletedTask;
        return RootCustomConverterRoundTrip();
    }

    internal static async Task<int> RootScalarRoundTripAsync()
    {
        await Task.CompletedTask;
        return RootScalarRoundTrip();
    }

    internal static async Task<int> RootUnicodeRoundTripAsync()
    {
        await Task.CompletedTask;
        return RootUnicodeRoundTrip();
    }

    internal static async Task<int> RootMalformedPayloadsAsync()
    {
        await Task.CompletedTask;
        return RootMalformedPayloads();
    }

    internal static async Task<int> RootMissingMetadataAsync()
    {
        await Task.CompletedTask;
        return RootMissingMetadata();
    }

    internal static async Task<int> RootExtensionDataAsync()
    {
        await Task.CompletedTask;
        return RootExtensionData();
    }

    internal static async Task<int> RootDocumentNavigationAsync()
    {
        await Task.CompletedTask;
        return RootDocumentNavigation();
    }

    internal static async Task<int> RootReaderWriterAsync()
    {
        await Task.CompletedTask;
        return RootReaderWriter();
    }

    internal static async Task<int> RootNamingPolicyAsync()
    {
        await Task.CompletedTask;
        return RootNamingPolicy();
    }

    private static int RootMetadataAccess()
    {
        var context = JsonTestContext.Default;
        return context.Document.Converter.Type == typeof(Document) &&
            context.NullableInt32.Converter.Type == typeof(int?) &&
            context.Marker.Converter.Type == typeof(Marker)
            ? 1
            : 0;
    }

    private static int RootDocumentRoundTrip()
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
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.Document);
        return actual is not null && actual.Name == expected.Name ? 1 : 0;
    }

    private static int RootNullableAndEnumRoundTrip()
    {
        var expected = new Document(
            "nullable",
            null,
            DocumentState.Complete,
            [],
            new Dictionary<string, int?>(),
            new Pair<Marker>(new(0), new(1)));
        var json = JsonSerializer.Serialize(expected, JsonTestContext.Default.Document);
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.Document);
        return actual is not null &&
            actual.Count is null &&
            actual.State == DocumentState.Complete
            ? 1
            : 0;
    }

    private static int RootCustomConverterRoundTrip()
    {
        var json = JsonSerializer.Serialize(new Marker(23), JsonTestContext.Default.Marker);
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.Marker);
        return actual.Value == 23 ? 1 : 0;
    }

    private static int RootScalarRoundTrip()
    {
        var expected = new ScalarDocument(
            new DateTime(2020, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc).AddTicks(9012),
            new DateTimeOffset(
                2020,
                1,
                2,
                3,
                4,
                5,
                678,
                TimeSpan.FromHours(2)).AddTicks(9012),
            new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            12.5000m,
            long.MaxValue,
            -1250.5d);
        var json = JsonSerializer.Serialize(
            expected,
            JsonTestContext.Default.ScalarDocument);
        var actual = JsonSerializer.Deserialize(
            json,
            JsonTestContext.Default.ScalarDocument);
        return actual is not null && actual.Id == expected.Id ? 1 : 0;
    }

    private static int RootUnicodeRoundTrip()
    {
        var expected = new Document(
            "A\"mé😀",
            1,
            DocumentState.Ready,
            [1],
            new Dictionary<string, int?> { ["A\"mé"] = 1 },
            new Pair<Marker>(new(1), new(2)));
        var json = JsonSerializer.Serialize(expected, JsonTestContext.Default.Document);
        var actual = JsonSerializer.Deserialize(json, JsonTestContext.Default.Document);
        return actual is not null && actual.Name == expected.Name ? 1 : 0;
    }

    private static int RootMalformedPayloads()
    {
        try
        {
            _ = JsonSerializer.Deserialize(
                "{\"Name\":\"broken\",\"Count\":not-a-number}",
                JsonTestContext.Default.Document);
            return 0;
        }
        catch (JsonException)
        {
            return 1;
        }
    }

    private static int RootMissingMetadata()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new EmptyTypeInfoResolver(),
        };
        try
        {
            _ = JsonSerializer.Serialize(new UnregisteredPayload(17), options);
            return 0;
        }
        catch (NotSupportedException)
        {
            return 1;
        }
    }

    private static int RootExtensionData()
    {
        const string source = "{\"Tail\":\"end\",\"extra\":17,\"Head\":\"start\"}";
        var envelope = JsonSerializer.Deserialize(
            source,
            JsonTestContext.Default.ExtensionEnvelope);
        return envelope is not null &&
            envelope.Head == "start" &&
            envelope.ExtensionData["extra"].GetInt32() == 17
            ? 1
            : 0;
    }

    private static int RootDocumentNavigation()
    {
        using var document = JsonDocument.Parse(
            " { \"name\":\"value\", \"items\":[1,true,null] } ");
        var root = document.RootElement;
        return root.GetProperty("name").GetString() == "value" &&
            root.GetProperty("items").GetArrayLength() == 3
            ? 1
            : 0;
    }

    private static int RootReaderWriter()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", 7);
            writer.WriteEndObject();
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        var tokenCount = 0;
        while (reader.Read())
        {
            tokenCount++;
        }

        return tokenCount == 4 ? 1 : 0;
    }

    private static int RootNamingPolicy()
    {
        var expected = new NamedDocument("portable", 12);
        var json = JsonSerializer.Serialize(
            expected,
            NamedJsonTestContext.Default.NamedDocument);
        var actual = JsonSerializer.Deserialize(
            json,
            NamedJsonTestContext.Default.NamedDocument);
        return actual is not null && actual.DisplayName == expected.DisplayName ? 1 : 0;
    }

    private sealed class EmptyTypeInfoResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }
}

public sealed class BaselineRootSuite
{
    public ValueTask MetadataAccess(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootMetadataAccessAsync(), cancellationToken);

    public ValueTask DocumentRoundTrip(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootDocumentRoundTripAsync(), cancellationToken);

    public ValueTask NullableAndEnumRoundTrip(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootNullableAndEnumRoundTripAsync(), cancellationToken);

    public ValueTask CustomConverterRoundTrip(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootCustomConverterRoundTripAsync(), cancellationToken);

    public ValueTask ScalarRoundTrip(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootScalarRoundTripAsync(), cancellationToken);

    public ValueTask UnicodeRoundTrip(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootUnicodeRoundTripAsync(), cancellationToken);

    public ValueTask MalformedPayloads(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootMalformedPayloadsAsync(), cancellationToken);

    public ValueTask MissingMetadata(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootMissingMetadataAsync(), cancellationToken);

    public ValueTask ExtensionData(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootExtensionDataAsync(), cancellationToken);

    public ValueTask DocumentNavigation(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootDocumentNavigationAsync(), cancellationToken);

    public ValueTask ReaderWriter(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootReaderWriterAsync(), cancellationToken);

    public ValueTask NamingPolicy(CancellationToken cancellationToken) =>
        Wrap(AdditionalRootCatalog.RootNamingPolicyAsync(), cancellationToken);

    private static ValueTask Wrap(Task<int> task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(task);
    }
}

public sealed class GeneratedMixedRootSuite
{
    public ValueTask Invoke(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

public abstract class LocalGeneratedCase
{
    protected LocalGeneratedCase(
        string stableId,
        string methodName,
        IEnumerable<string> categories,
        IEnumerable<string> properties,
        IEnumerable<string> dependencies,
        LocalGeneratedCaseRow row)
    {
        StableId = stableId;
        MethodName = methodName;
        Categories = Copy(categories);
        Properties = Copy(properties);
        Dependencies = Copy(dependencies);
        Row = row;
    }

    public string StableId { get; }

    public string MethodName { get; }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<string> Properties { get; }

    public IReadOnlyList<string> Dependencies { get; }

    public LocalGeneratedCaseRow Row { get; }

    public abstract ValueTask ExecuteAsync(CancellationToken cancellationToken);

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(new List<T>(values));
}

public sealed class LocalGeneratedCase<T> : LocalGeneratedCase
    where T : class
{
    private readonly Func<T> _createInstance;
    private readonly Func<T, CancellationToken, ValueTask> _invoke;

    public LocalGeneratedCase(
        string stableId,
        string methodName,
        Func<T> createInstance,
        Func<T, CancellationToken, ValueTask> invoke,
        IEnumerable<string> categories,
        IEnumerable<string> properties,
        IEnumerable<string> dependencies,
        LocalGeneratedCaseRow row)
        : base(stableId, methodName, categories, properties, dependencies, row)
    {
        _createInstance = createInstance;
        _invoke = invoke;
    }

    public override ValueTask ExecuteAsync(CancellationToken cancellationToken) =>
        _invoke(_createInstance(), cancellationToken);
}

public sealed class LocalGeneratedCaseRow(
    string stableId,
    string displayName,
    object?[] arguments)
{
    public string StableId { get; } = stableId;

    public string DisplayName { get; } = displayName;

    public IReadOnlyList<object?> Arguments { get; } =
        new ReadOnlyCollection<object?>(arguments);
}

public sealed class LocalGeneratedCatalog
{
    public LocalGeneratedCatalog(IEnumerable<LocalGeneratedCase> cases)
    {
        var snapshot = new List<LocalGeneratedCase>(cases).ToArray();
        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testCase in snapshot)
        {
            if (!stableIds.Add(testCase.StableId))
            {
                throw new InvalidOperationException("Duplicate local stable ID.");
            }
        }

        Array.Sort(
            snapshot,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.StableId,
                right.StableId));
        Cases = new ReadOnlyCollection<LocalGeneratedCase>(snapshot);
    }

    public IReadOnlyList<LocalGeneratedCase> Cases { get; }
}

public static class LocalGeneratedCatalogRoot
{
    private static readonly LocalGeneratedCatalog Catalog = CreateCatalog();

    public static LocalGeneratedCatalog GetCatalog() => Catalog;

    private static LocalGeneratedCatalog CreateCatalog()
    {
        var cases = new List<LocalGeneratedCase>
        {
            CreateBaselineCase("01", "MetadataAccess", static (suite, token) =>
                suite.MetadataAccess(token)),
            CreateBaselineCase("02", "DocumentRoundTrip", static (suite, token) =>
                suite.DocumentRoundTrip(token)),
            CreateBaselineCase("03", "NullableAndEnumRoundTrip", static (suite, token) =>
                suite.NullableAndEnumRoundTrip(token)),
            CreateBaselineCase("04", "CustomConverterRoundTrip", static (suite, token) =>
                suite.CustomConverterRoundTrip(token)),
            CreateBaselineCase("05", "ScalarRoundTrip", static (suite, token) =>
                suite.ScalarRoundTrip(token)),
            CreateBaselineCase("06", "UnicodeRoundTrip", static (suite, token) =>
                suite.UnicodeRoundTrip(token)),
            CreateBaselineCase("07", "MalformedPayloads", static (suite, token) =>
                suite.MalformedPayloads(token)),
            CreateBaselineCase("08", "MissingMetadata", static (suite, token) =>
                suite.MissingMetadata(token)),
            CreateBaselineCase("09", "ExtensionData", static (suite, token) =>
                suite.ExtensionData(token)),
            CreateBaselineCase("10", "DocumentNavigation", static (suite, token) =>
                suite.DocumentNavigation(token)),
            CreateBaselineCase("11", "ReaderWriter", static (suite, token) =>
                suite.ReaderWriter(token)),
            CreateBaselineCase("12", "NamingPolicy", static (suite, token) =>
                suite.NamingPolicy(token)),
            new LocalGeneratedCase<GeneratedMixedRootSuite>(
                "00",
                "GeneratedMixedShapeRoundTrips",
                static () => new GeneratedMixedRootSuite(),
                static (suite, token) => suite.Invoke(token),
                [],
                [],
                [],
                new LocalGeneratedCaseRow(
                    "00",
                    "GeneratedMixedShapeRoundTrips()",
                    [])),
        };

        return new LocalGeneratedCatalog(cases);
    }

    private static LocalGeneratedCase<BaselineRootSuite> CreateBaselineCase(
        string stableId,
        string methodName,
        Func<BaselineRootSuite, CancellationToken, ValueTask> invoke) =>
        new(
            stableId,
            methodName,
            static () => new BaselineRootSuite(),
            invoke,
            [],
            [],
            [],
            new LocalGeneratedCaseRow(stableId, methodName + "()", []));
}

public static class EntryPoint
{
    public static int Run(int input)
    {
        if (!ObjectArrayKeepsStoredReferenceAlive())
        {
            return -400;
        }

        if (LocalGeneratedCatalogRoot.GetCatalog().Cases.Count != 13)
        {
            return -100;
        }

        var expected = new GeneratedMixedModel(
            "text",
            7,
            new GeneratedMixedChild("first"),
            ["item"],
            new Dictionary<string, int> { ["key"] = 42 },
            new GeneratedMixedChild("second"));

        var json = JsonSerializer.Serialize(
            expected,
            GeneratedMixedContext.Default.GeneratedMixedModel);

        GeneratedMixedModel? actual;
        try
        {
            actual = JsonSerializer.Deserialize(
                json,
                GeneratedMixedContext.Default.GeneratedMixedModel);
        }
        catch
        {
            return -200;
        }

        return actual is not null &&
            actual.Text == expected.Text &&
            actual.Number == expected.Number &&
            actual.First.Value == expected.First.Value &&
            actual.Second.Value == expected.Second.Value &&
            actual.Items.Count == 1 &&
            actual.Items[0] == "item" &&
            actual.Map.Count == 1 &&
            actual.Map["key"] == 42
            ? 1000 + input
            : -300;
    }

    private static bool ObjectArrayKeepsStoredReferenceAlive()
    {
        var frame = new ArgumentFrame
        {
            State = new ArgumentState { Arguments = new object?[6] },
        };
        ((object?[])frame.State.Arguments!)[2] = new GeneratedMixedChild("rooted");

        IArgumentReader reader = new AllocatingArgumentReader();
        object third = reader.Read(ref frame);
        ((object?[])frame.State.Arguments!)[3] = third;

        return ((object?[])frame.State.Arguments!)[2] is GeneratedMixedChild child &&
            child.Value == "rooted";
    }

    private interface IArgumentReader
    {
        object Read(ref ArgumentFrame frame);
    }

    private sealed class AllocatingArgumentReader : IArgumentReader
    {
        public object Read(ref ArgumentFrame frame)
        {
            GC.Collect();
            return new List<string> { "allocation" };
        }
    }

    private sealed class ArgumentState
    {
        public object? Arguments;
    }

    private struct ArgumentFrame
    {
        public ArgumentState? State;
    }
}
