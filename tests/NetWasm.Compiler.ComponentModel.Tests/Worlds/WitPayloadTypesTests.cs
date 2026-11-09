using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Tests.Worlds;

public sealed class WitPayloadTypesTests
{
    [Fact]
    public void NumbersAParametersStreamsAndFuturesBeforeItsResultsEachInnerTypeFirst()
    {
        var document = new WitDocument([], [], [], [
            Type(0, """{"stream":"u8"}"""),
            Type(1, """{"future":"u32"}"""),
            Type(2, """{"record":{"fields":[{"name":"body","type":0},{"name":"size","type":"u64"}]}}"""),
            Type(3, """{"tuple":{"types":[1,"u32"]}}"""),
            Type(4, """{"variant":{"cases":[{"name":"data","type":0},{"name":"none","type":null}]}}"""),
            Type(5, """{"option":1}"""),
            Type(6, """{"result":{"ok":0,"err":1}}"""),
            Type(7, """{"type":0}"""),
            Type(8, """{"list":1}"""),
            Type(9, """{"enum":{"cases":[{"name":"a"}]}}"""),
            Type(10, """{"future":11}"""),
            Type(11, """{"stream":null}"""),
            Type(12, """{"result":{"ok":null,"err":null}}"""),
            Type(13, "\"resource\""),
        ], "{}");
        var function = new WitFunction("send", [
            new("a", new WitTypeReference.Defined(2)),
            new("b", new WitTypeReference.Defined(3)),
            new("c", new WitTypeReference.Defined(4)),
            new("d", new WitTypeReference.Defined(5)),
            new("e", new WitTypeReference.Defined(6)),
            new("f", new WitTypeReference.Defined(7)),
            new("g", new WitTypeReference.Defined(8)),
            new("h", new WitTypeReference.Defined(9)),
            new("i", new WitTypeReference.Primitive("string")),
            new("j", new WitTypeReference.Defined(12)),
            new("k", new WitTypeReference.Defined(13)),
        ], new WitTypeReference.Defined(10), new("freestanding"));

        Assert.Equal([0, 1, 0, 1, 0, 1, 0, 1, 11, 10], WitPayloadTypes.Find(document, function));
        Assert.True(WitPayloadTypes.IsStream(document, 0));
        Assert.False(WitPayloadTypes.IsStream(document, 10));
        Assert.Empty(WitPayloadTypes.Find(document, new WitFunction("none", [], null, new("freestanding"))));
    }

    private static WitTypeDefinition Type(int id, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, null, document.RootElement.Clone(), null);
    }
}
