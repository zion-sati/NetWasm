using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitCanonicalTypeReachabilityResolverTests
{
    [Fact]
    public void ResolvesTransitiveTypesThroughImportedAndExportedFunctions()
    {
        var types = ImmutableArray.Create(
            Type(0, "root", "{\"record\":{\"fields\":[{\"name\":\"nested\",\"type\":1},{\"name\":\"value\",\"type\":\"u32\"}]}}"),
            Type(1, null, "{\"list\":2}"),
            Type(2, "choice", "{\"variant\":{\"cases\":[{\"name\":\"none\",\"type\":null},{\"name\":\"handle\",\"type\":3}]}}"),
            Type(3, null, "{\"handle\":{\"own\":4}}"),
            Type(4, "color", "{\"enum\":{\"cases\":[{\"name\":\"red\"}]}}"),
            Type(5, null, "{\"result\":{\"ok\":6,\"err\":null}}"),
            Type(6, null, "{\"tuple\":{\"types\":[0,\"string\"]}}"),
            Type(7, "unreachable", "{\"record\":{\"fields\":[{\"name\":\"value\",\"type\":\"u8\"}]}}"));
        var function = new WitFunction(
            "read",
            [
                new WitParameter("value", new WitTypeReference.Defined(0)),
                new WitParameter("primitive", new WitTypeReference.Primitive("u32")),
            ],
            new WitTypeReference.Defined(5),
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty
                .Add("root", 0)
                .Add("choice", 2)
                .Add("color", 4),
            [function]);
        var exported = new WitFunction(
            "write",
            [new WitParameter("value", new WitTypeReference.Defined(0))],
            new WitTypeReference.Defined(6),
            new WitFunctionKind("freestanding"));
        var world = new WitWorld(
            0,
            "main",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            [new WitWorldItem("write", null, exported)]);
        var document = new WitDocument([], [@interface], [world], types, "{}");

#pragma warning disable CA1859 // This test verifies the resolver contract dispatch.
        IWitCanonicalTypeReachabilityResolver resolver =
            new WitCanonicalTypeReachabilityResolver();
#pragma warning restore CA1859
        var reachable = resolver.Resolve(document, world);

        Assert.Equal(ImmutableArray.Create(0, 1, 2, 3, 4, 5, 6), reachable);
        Assert.DoesNotContain(7, reachable);
    }

    private static WitTypeDefinition Type(int id, string? name, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new WitTypeDefinition(id, name, document.RootElement.Clone(), null);
    }
}
