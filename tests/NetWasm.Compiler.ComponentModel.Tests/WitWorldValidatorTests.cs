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

    // WASI 0.3 worlds declare asynchronous functions, streams and futures; a
    // world is valid with them, and only a binding decides what NetWasm supports.
    [Theory]
    [InlineData("async-freestanding", "{\"stream\":\"u8\"}")]
    [InlineData("async-method", "{\"future\":null}")]
    [InlineData("freestanding", "{\"stream\":null}")]
    public void AcceptsAsynchronousFunctionsStreamsAndFutures(string kind, string type)
    {
        var document = CreateDocument(
            new WitFunction("run", [], null, new WitFunctionKind(kind)),
            Json(type));

        new WitWorldValidator().Validate(document, document.Worlds[0]);
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
