using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WitWorldValidatorTests
{
    [Fact]
    public void AcceptsSynchronousFunctionsAndTypes()
    {
        var document = CreateDocument(
            new WitFunction("run", [], null, new WitFunctionKind("freestanding")),
            Json("{\"list\":\"u8\"}"));

        new WitWorldValidator().Validate(document, document.Worlds[0]);
    }

    [Fact]
    public void AcceptsResourceTypeDefinitions()
    {
        var document = CreateDocument(
            new WitFunction("run", [], null, new WitFunctionKind("freestanding")),
            Json("\"resource\""));

        new WitWorldValidator().Validate(document, document.Worlds[0]);
    }

    [Theory]
    [InlineData("async-freestanding")]
    [InlineData("async-method")]
    public void RejectsUserDefinedAsyncFunctions(string kind)
    {
        var document = CreateDocument(
            new WitFunction("run", [], null, new WitFunctionKind(kind)),
            Json("{\"list\":\"u8\"}"));

        var exception = Assert.Throws<CompilerException>(() =>
            new WitWorldValidator().Validate(document, document.Worlds[0]));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("asynchronous WIT function", exception.Diagnostic.Message);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("stream")]
    public void RejectsDeferredUserDefinedTypes(string kind)
    {
        var document = CreateDocument(
            new WitFunction("run", [], null, new WitFunctionKind("freestanding")),
            Json($"{{\"{kind}\":null}}"));

        var exception = Assert.Throws<CompilerException>(() =>
            new WitWorldValidator().Validate(document, document.Worlds[0]));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("future and stream", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsFlagsBeyondTheComponentModelMaximum()
    {
        var flags = string.Join(",", Enumerable.Range(0, 33)
            .Select(index => $"{{\"name\":\"p{index}\"}}"));
        var document = CreateDocument(
            new WitFunction("run", [], null, new WitFunctionKind("freestanding")),
            Json($"{{\"flags\":{{\"flags\":[{flags}]}}}}"));

        var exception = Assert.Throws<CompilerException>(() =>
            new WitWorldValidator().Validate(document, document.Worlds[0]));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("Component Model maximum of 32 flags", exception.Diagnostic.Message);
    }

    private static WitDocument CreateDocument(
        WitFunction function,
        JsonElement type)
    {
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty,
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        return new WitDocument(
            [],
            [@interface],
            [world],
            [new WitTypeDefinition(0, null, type, null)],
            "{}");
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
