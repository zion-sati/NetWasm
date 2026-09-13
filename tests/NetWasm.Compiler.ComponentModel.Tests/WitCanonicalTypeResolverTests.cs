using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WitCanonicalTypeResolverTests
{
    private readonly WitCanonicalTypeResolver _resolver = new();

    [Fact]
    public void ResolvesNestedCanonicalShapesAndResourceOwnership()
    {
        var document = Document(
            Type(0, "item", """{"record":{"fields":[{"name":"name","type":"string"}]}}"""),
            Type(1, null, """{"list":0}"""),
            Type(2, null, """{"option":1}"""),
            Type(3, "file", "\"resource\""),
            Type(4, null, """{"handle":{"borrow":3}}"""));

        var option = _resolver.Resolve(document, new WitTypeReference.Defined(2));
        var resource = _resolver.Resolve(document, new WitTypeReference.Defined(4));

        Assert.Equal(CanonicalAbiTypeKind.Option, option.Kind);
        Assert.Equal(CanonicalAbiTypeKind.List, option.ElementType!.Kind);
        Assert.Equal(CanonicalAbiTypeKind.Record, option.ElementType.ElementType!.Kind);
        Assert.Equal(CanonicalAbiTypeKind.Text,
            option.ElementType.ElementType.Fields[0].Type.Kind);
        Assert.Equal(CanonicalAbiTypeKind.BorrowedResource, resource.Kind);
        Assert.Equal(3, resource.ResourceTypeId);
    }

    [Fact]
    public void ResolvesResourceHandleThroughImportedTypeAlias()
    {
        var document = Document(
            Type(0, "pollable", "\"resource\""),
            Type(1, "pollable", """{"type":0}"""),
            Type(2, null, """{"handle":{"own":1}}"""));

        var resource = _resolver.Resolve(
            document,
            new WitTypeReference.Defined(2));

        Assert.Equal(CanonicalAbiTypeKind.OwnedResource, resource.Kind);
        Assert.Equal(0, resource.ResourceTypeId);
    }

    [Fact]
    public void RejectsResourceHandleAliasCycle()
    {
        var document = Document(
            Type(0, "left", """{"type":1}"""),
            Type(1, "right", """{"type":0}"""),
            Type(2, null, """{"handle":{"borrow":0}}"""));

        var exception = Assert.Throws<CompilerException>(() =>
            _resolver.Resolve(document, new WitTypeReference.Defined(2)));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("resource type", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsUserDefinedFutureBeforeLowering()
    {
        var document = Document(Type(0, null, """{"future":"u32"}"""));

        var exception = Assert.Throws<CompilerException>(() =>
            _resolver.Resolve(document, new WitTypeReference.Defined(0)));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("future", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsUserDefinedStreamBeforeLowering()
    {
        var document = Document(Type(0, null, "{\"stream\":\"u32\"}"));

        var exception = Assert.Throws<CompilerException>(() =>
            _resolver.Resolve(document, new WitTypeReference.Defined(0)));

        Assert.Contains("stream", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsInvalidReferencesKindsPrimitivesAndHandles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _resolver.Resolve(
            Document(), new UnknownReference()));
        Assert.Throws<CompilerException>(() => _resolver.Resolve(
            Document(Type(0, null, "\"not-resource\"")),
            new WitTypeReference.Defined(0)));
        Assert.Throws<CompilerException>(() => _resolver.Resolve(
            Document(
                Type(0, null, "\"not-resource\""),
                Type(1, null, "{\"handle\":{\"own\":0}}")),
            new WitTypeReference.Defined(1)));
        Assert.Throws<CompilerException>(() => _resolver.Resolve(
            Document(Type(0, null, "{\"mystery\":\"u32\"}")),
            new WitTypeReference.Defined(0)));
        Assert.Throws<CompilerException>(() => _resolver.Resolve(
            Document(Type(0, null, "{\"handle\":{\"own\":99}}")),
            new WitTypeReference.Defined(0)));
        Assert.Throws<CompilerException>(() => _resolver.Resolve(
            Document(
                Type(0, null, "{\"handle\":{\"borrow\":1}}"),
                Type(1, null, "{\"list\":\"string\"}")),
            new WitTypeReference.Defined(0)));
        Assert.Throws<CompilerException>(() => _resolver.Resolve(
            Document(), new WitTypeReference.Primitive("i128")));
    }

    private static WitDocument Document(params WitTypeDefinition[] types) => new(
        [],
        [],
        [],
        [.. types],
        "{}");

    private static WitTypeDefinition Type(
        int id,
        string? name,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), null);
    }

    private sealed record UnknownReference : WitTypeReference;
}
