using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusObservationResponseParserTests
{
    [Fact]
    public void ParsePreservesOrderedValuesExceptionsTrapsAndTracePayloads()
    {
        var request = CreateRequest();
        var json = """
            {"schemaVersion":1,"target":"wasm32","moduleSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
             "manifestSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","observations":[
              {"input":-1,"kind":"value","value":42,"exceptionTypeId":null,"trace":-7,"traceRecords":[]},
              {"input":0,"kind":"exception","value":null,"exceptionTypeId":17,"trace":1,"traceRecords":[
                {"kind":3,"eventId":5,"payloadLow":-1,"payloadHigh":1}]},
              {"input":1,"kind":"trap","value":null,"exceptionTypeId":null,"trace":0,"traceRecords":[]}]}
            """;

        var result = Parser().Parse(json, request,
            ImmutableDictionary<int, string>.Empty.Add(17, "System.OverflowException"));

        Assert.Equal(request.ModuleSha256, result.ModuleSha256);
        Assert.Equal(request.ManifestSha256, result.ManifestSha256);
        Assert.Equal([-1, 0, 1], result.Observations.Keys.Order());
        Assert.Equal(OracleObservationKind.Value, result.Observations[-1].Kind);
        Assert.Equal(42, result.Observations[-1].Value);
        Assert.Equal([new TraceRecord(TraceRecordKind.StateChecksum, 0, -7)],
            result.Observations[-1].TraceRecords.AsEnumerable());
        Assert.Equal(OracleObservationKind.ManagedException, result.Observations[0].Kind);
        Assert.Equal("System.OverflowException", result.Observations[0].ExceptionType);
        Assert.Equal(new TraceRecord(TraceRecordKind.Int64, 5, 0x00000001FFFFFFFF),
            Assert.Single(result.Observations[0].TraceRecords));
        Assert.Equal(OracleObservationKind.Trap, result.Observations[1].Kind);
        Assert.Null(result.Observations[1].ExceptionType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ParseRejectsMissingArguments(string? json)
    {
        var request = CreateRequest();
        Assert.ThrowsAny<ArgumentException>(() => Parser().Parse(json!, request, ImmutableDictionary<int, string>.Empty));
        Assert.Throws<ArgumentNullException>(() => Parser().Parse("{}", null!, ImmutableDictionary<int, string>.Empty));
        Assert.Throws<ArgumentNullException>(() => Parser().Parse("{}", request, null!));
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("target", "\"wasm64\"")]
    [InlineData("moduleSha256", "\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"")]
    [InlineData("manifestSha256", "\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"")]
    public void ParseRejectsResponseIdentityChanges(string property, string replacement)
    {
        var request = CreateRequest();
        var document = ValidDocument();
        document = property switch
        {
            "schemaVersion" => document.Replace("\"schemaVersion\":1", $"\"schemaVersion\":{replacement}", StringComparison.Ordinal),
            "target" => document.Replace("\"target\":\"wasm32\"", $"\"target\":{replacement}", StringComparison.Ordinal),
            "moduleSha256" => document.Replace($"\"moduleSha256\":\"{request.ModuleSha256}\"", $"\"moduleSha256\":{replacement}", StringComparison.Ordinal),
            _ => document.Replace($"\"manifestSha256\":\"{request.ManifestSha256}\"", $"\"manifestSha256\":{replacement}", StringComparison.Ordinal),
        };
        Assert.Throws<JsonException>(() => Parser().Parse(document, request, ImmutableDictionary<int, string>.Empty));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"input\":-1,\"kind\":\"value\",\"value\":1,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":0,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":1,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":2,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}")]
    public void ParseRejectsObservationCountChanges(string observations) =>
        Assert.Throws<JsonException>(() => Parser().Parse(
            ValidDocument(observations), CreateRequest(), ImmutableDictionary<int, string>.Empty));

    [Theory]
    [InlineData("{\"input\":0,\"kind\":\"value\",\"value\":1,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"value\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"value\",\"value\":1,\"exceptionTypeId\":17,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"exception\",\"value\":1,\"exceptionTypeId\":17,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"exception\",\"value\":null,\"exceptionTypeId\":0,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"exception\",\"value\":null,\"exceptionTypeId\":18,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"trap\",\"value\":1,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":17,\"trace\":0,\"traceRecords\":[]}")]
    [InlineData("{\"input\":-1,\"kind\":\"unknown\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}")]
    public void ParseRejectsReorderingAndInconsistentOutcomes(string first)
    {
        var tail = "{\"input\":0,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":1,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}";
        Assert.Throws<JsonException>(() => Parser().Parse(ValidDocument($"[{first},{tail}]"),
            CreateRequest(), ImmutableDictionary<int, string>.Empty.Add(17, "System.Exception")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[{\"kind\":99,\"eventId\":0,\"payloadLow\":0,\"payloadHigh\":0}]")]
    public void ParseRejectsMissingOrUnknownTraceRecords(string records)
    {
        var first = $"{{\"input\":-1,\"kind\":\"value\",\"value\":1,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":{records}}}";
        var tail = "{\"input\":0,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":1,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}";
        Assert.Throws<JsonException>(() => Parser().Parse(ValidDocument($"[{first},{tail}]"),
            CreateRequest(), ImmutableDictionary<int, string>.Empty));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"wasm32\",\"moduleSha256\":\"a\",\"manifestSha256\":\"b\",\"observations\":[],\"unknown\":1}")]
    public void ParseRejectsMalformedDocuments(string json) =>
        Assert.Throws<JsonException>(() => Parser().Parse(json, CreateRequest(), ImmutableDictionary<int, string>.Empty));

    internal static LinkedCorpusObservationRequest CreateRequest() =>
        new(1, "wasm32", "module.wasm", "manifest.json",
            new string('a', 64), new string('b', 64), [-1, 0, 1], true, true);

    private static ILinkedCorpusObservationResponseParser Parser() =>
        Assert.IsAssignableFrom<ILinkedCorpusObservationResponseParser>(new LinkedCorpusObservationResponseParser());

    private static string ValidDocument(string? observations = null) => $$"""
        {"schemaVersion":1,"target":"wasm32","moduleSha256":"{{new string('a', 64)}}",
         "manifestSha256":"{{new string('b', 64)}}","observations":{{observations ?? "[{\"input\":-1,\"kind\":\"value\",\"value\":1,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":0,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]},{\"input\":1,\"kind\":\"trap\",\"value\":null,\"exceptionTypeId\":null,\"trace\":0,\"traceRecords\":[]}]"}}}
        """;
}
