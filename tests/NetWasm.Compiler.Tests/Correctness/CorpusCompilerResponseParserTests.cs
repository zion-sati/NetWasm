using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilerResponseParserTests
{
    [Theory]
    [InlineData("TypeNames", "ModuleSha256", "StaticDataEnd")]
    [InlineData("typeNames", "moduleSha256", "staticDataEnd")]
    public void ParsesCompleteResponseWithoutChangingItsValues(string types, string hash, string data)
    {
        var json = $$"""{"{{types}}":{"7":"System.Exception"},"{{hash}}":"recorded-hash","{{data}}":32}""";
        var result = Parser().Parse(json);

        Assert.Equal("System.Exception", Assert.Single(result.TypeNames).Value);
        Assert.Equal(7, Assert.Single(result.TypeNames).Key);
        Assert.Equal("recorded-hash", result.ModuleSha256);
        Assert.Equal(32, result.StaticDataEnd);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingText(string? json) =>
        Assert.ThrowsAny<ArgumentException>(() => Parser().Parse(json!));

    [Fact]
    public void PreservesOptionalHostTelemetryWithoutRequiringIt()
    {
        var result = Parser().Parse("""
            {"TypeNames":{},"ModuleSha256":"hash","StaticDataEnd":0,
             "AdapterDuration":"00:00:01","CompilerTiming":{"Elapsed":2},"CompilerMetrics":null}
            """);

        Assert.Equal(TimeSpan.FromSeconds(1), result.AdapterDuration);
        Assert.Equal(2, result.CompilerTiming!.Value.GetProperty("Elapsed").GetInt32());
        Assert.Null(result.CompilerMetrics);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"TypeNames\":{},\"ModuleSha256\":\"hash\"}")]
    [InlineData("{\"TypeNames\":{},\"StaticDataEnd\":0}")]
    [InlineData("{\"ModuleSha256\":\"hash\",\"StaticDataEnd\":0}")]
    [InlineData("{\"TypeNames\":null,\"ModuleSha256\":\"hash\",\"StaticDataEnd\":0}")]
    [InlineData("{\"TypeNames\":{},\"ModuleSha256\":null,\"StaticDataEnd\":0}")]
    [InlineData("{\"TypeNames\":{},\"ModuleSha256\":\"hash\",\"StaticDataEnd\":null}")]
    [InlineData("{\"TypeNames\":{},\"ModuleSha256\":\"hash\",\"StaticDataEnd\":0,\"StaticDataEnd\":1}")]
    [InlineData("{\"TypeNames\":{},\"ModuleSha256\":\"hash\",\"StaticDataEnd\":0,\"Unknown\":1}")]
    [InlineData("{\"TypeNames\":{},\"ModuleSha256\":\"hash\",\"StaticDataEnd\":2147483648}")]
    public void RejectsMalformedOrIncompleteResponse(string json) =>
        Assert.Throws<JsonException>(() => Parser().Parse(json));

    private static ICorpusCompilerResponseParser Parser() =>
        Assert.IsAssignableFrom<ICorpusCompilerResponseParser>(new CorpusCompilerResponseParser());
}
