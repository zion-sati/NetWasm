using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WitCanonicalTypeReachabilityResolverTests
{
    [Fact]
    public void ResolvesTypesReachableFromWorldFunctionsInStableOrder()
    {
        var types = ImmutableArray.Create(
            Type(0, "root", "{\"record\":{\"fields\":[{\"name\":\"item\",\"type\":1}]}}"),
            Type(1, "item", "{\"list\":\"u8\"}"),
            Type(2, "unused", "{\"list\":\"u8\"}"));
        var function = new WitFunction(
            "run",
            [],
            new WitTypeReference.Defined(0),
            new WitFunctionKind("freestanding"));
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
        var document = new WitDocument([], [@interface], [world], types, "{}");

        var resolved = Resolve(
            new WitCanonicalTypeReachabilityResolver(),
            document,
            world);

        Assert.Equal([0, 1], resolved);
    }

    private static ImmutableArray<int> Resolve(
        object resolver,
        WitDocument document,
        WitWorld world) => ((IWitCanonicalTypeReachabilityResolver)resolver)
        .Resolve(document, world);

    private static WitTypeDefinition Type(int id, string? name, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), 0);
    }
}
