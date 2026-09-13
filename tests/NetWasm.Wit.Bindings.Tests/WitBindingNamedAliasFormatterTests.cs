using System;
using System.Text.Json;
using NetWasm.Wit.Bindings.TypeDefinitions;
using Xunit;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingNamedAliasFormatterTests
{
    [Fact]
    public void ConstructorRejectsNullTypeDefinitionClassifier()
    {
        Assert.Throws<ArgumentNullException>(() => new WitBindingSyntaxFormatter(null!));
    }

    [Theory]
    [InlineData("{\"type\":\"u8\"}")]
    [InlineData("{\"list\":\"u8\"}")]
    [InlineData("{\"option\":\"u8\"}")]
    [InlineData("{\"tuple\":{\"types\":[\"s32\",\"f64\"]}}")]
    [InlineData("{\"result\":{\"ok\":\"u8\",\"err\":\"string\"}}")]
    public void FormatResolvesNamedTransparentAliasLikeItsDefinedId(string json)
    {
        var definition = Definition("alias", json);
        var anonymousDefinition = Definition(null, json);
        var document = new WitDocument(default!, default!, default!, [definition, anonymousDefinition], default!);
        var formatter = new IWitBindingSyntaxFormatter[]
        {
            new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
        }[0];

        var byName = formatter.Format(
            new WitBindingSyntaxRequest.TypeName(document, new WitTypeReference.Primitive("alias")));
        var byId = formatter.Format(
            new WitBindingSyntaxRequest.TypeName(document, new WitTypeReference.Defined(0)));
        var byAnonymous = formatter.Format(
            new WitBindingSyntaxRequest.TypeName(document, new WitTypeReference.Defined(1)));

        Assert.Equal(byId, byName);
        Assert.Equal(byAnonymous, byName);
    }

    private static WitTypeDefinition Definition(string? name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var shape = document.RootElement.Clone();
        return new WitTypeDefinition(0, name, shape, 0);
    }
}
