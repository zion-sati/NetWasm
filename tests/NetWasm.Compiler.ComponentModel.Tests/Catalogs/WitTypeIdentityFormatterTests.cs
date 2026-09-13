using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Catalogs;

public sealed class WitTypeIdentityFormatterTests
{
    [Theory]
    [InlineData("bool")]
    [InlineData("s8")]
    [InlineData("u8")]
    [InlineData("s16")]
    [InlineData("u16")]
    [InlineData("s32")]
    [InlineData("u32")]
    [InlineData("s64")]
    [InlineData("u64")]
    [InlineData("f32")]
    [InlineData("f64")]
    [InlineData("char")]
    [InlineData("string")]
    public void PreservesCanonicalPrimitiveNames(string name)
    {
        Assert.Equal(name, Create().Format(Document(), new WitTypeReference.Primitive(name)));
    }

    [Fact]
    public void FormatsNamedAndAnonymousStructuralIdentities()
    {
        var document = Document(
            Type(0, "item", """{"record":{"fields":[{"name":"value","type":"u32"}]}}""", 0),
            Type(1, null, """{"list":0}"""),
            Type(2, null, """{"record":{"fields":[{"name":"name","type":"string"},{"name":"item","type":0}]}}"""),
            Type(3, null, """{"tuple":{"types":["u8",0]}}"""),
            Type(4, null, """{"option":0}"""),
            Type(5, null, """{"result":{"ok":"u32","err":null}}"""),
            Type(6, null, """{"variant":{"cases":[{"name":"none","type":null},{"name":"some","type":0}]}}"""),
            Type(7, null, """{"enum":{"cases":[{"name":"red"},{"name":"blue"}]}}"""),
            Type(8, null, """{"flags":{"flags":[{"name":"read"},{"name":"write"}]}}"""),
            Type(9, "file", "\"resource\"", 0),
            Type(10, null, """{"handle":{"own":9}}"""),
            Type(11, "file-alias", """{"type":9}""", 0),
            Type(12, null, """{"handle":{"borrow":11}}"""),
            Type(13, null, """{"type":1}"""));
        var formatter = Create();

        Assert.Collection(
            Enumerable.Range(0, document.Types.Length)
                .Select(id => formatter.Format(document, new WitTypeReference.Defined(id))),
            value => Assert.Equal("wasi:test/api@1.2.3#item", value),
            value => Assert.Equal("list<wasi:test/api@1.2.3#item>", value),
            value => Assert.Equal("record<name:string,item:wasi:test/api@1.2.3#item>", value),
            value => Assert.Equal("tuple<u8,wasi:test/api@1.2.3#item>", value),
            value => Assert.Equal("option<wasi:test/api@1.2.3#item>", value),
            value => Assert.Equal("result<u32,unit>", value),
            value => Assert.Equal("variant<none,some:wasi:test/api@1.2.3#item>", value),
            value => Assert.Equal("enum<red,blue>", value),
            value => Assert.Equal("flags<read,write>", value),
            value => Assert.Equal("wasi:test/api@1.2.3#file", value),
            value => Assert.Equal("own<wasi:test/api@1.2.3#file>", value),
            value => Assert.Equal("wasi:test/api@1.2.3#file-alias", value),
            value => Assert.Equal("borrow<wasi:test/api@1.2.3#file>", value),
            value => Assert.Equal("list<wasi:test/api@1.2.3#item>", value));
    }

    [Fact]
    public void RejectsInvalidArgumentsCollectionsReferencesAndNamedOwners()
    {
        var formatter = Create();
        var valid = Document();

        Assert.Throws<ArgumentNullException>(() => new WitTypeIdentityFormatter(null!));
        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!, new WitTypeReference.Primitive("u8")));
        Assert.Throws<ArgumentNullException>(() => formatter.Format(valid, null!));
        AssertContractFailure(() => formatter.Format(
            valid with { Types = default }, new WitTypeReference.Primitive("u8")));
        AssertContractFailure(() => formatter.Format(
            valid with { Interfaces = default }, new WitTypeReference.Primitive("u8")));
        Assert.Throws<ArgumentOutOfRangeException>(() => formatter.Format(valid, new UnknownReference()));
        AssertContractFailure(() => formatter.Format(valid, new WitTypeReference.Primitive("i128")));
        AssertContractFailure(() => formatter.Format(valid, new WitTypeReference.Defined(0)));
        AssertContractFailure(() => formatter.Format(
            Document(Type(0, "item", """{"list":"u8"}""")),
            new WitTypeReference.Defined(0)));
        AssertContractFailure(() => formatter.Format(
            Document(Type(0, "item", """{"list":"u8"}""", 4)),
            new WitTypeReference.Defined(0)));
        AssertContractFailure(() => formatter.Format(
            DocumentWithInterfaces([null!], Type(0, "item", """{"list":"u8"}""", 0)),
            new WitTypeReference.Defined(0)));
        AssertContractFailure(() => formatter.Format(
            DocumentWithInterfaces(
                [new(0, "api", "wasi:test@1.2.3", [], [])],
                Type(0, " ", """{"list":"u8"}""", 0)),
            new WitTypeReference.Defined(0)));
        AssertContractFailure(() => formatter.Format(
            valid with { Types = [null!] },
            new WitTypeReference.Defined(0)));
    }

    [Fact]
    public void RejectsAnonymousAndResourceCycles()
    {
        AssertContractFailure(() => Create().Format(
            Document(Type(0, null, """{"type":1}"""), Type(1, null, """{"type":0}""")),
            new WitTypeReference.Defined(0)));
        AssertContractFailure(() => Create().Format(
            Document(
                Type(0, "left", """{"type":1}""", 0),
                Type(1, "right", """{"type":0}""", 0),
                Type(2, null, """{"handle":{"own":0}}""")),
            new WitTypeReference.Defined(2)));
    }

    [Theory]
    [InlineData("\"resource\"")]
    [InlineData("{}")]
    [InlineData("{\"list\":\"u8\",\"option\":\"u8\"}")]
    [InlineData("{\"mystery\":\"u8\"}")]
    [InlineData("{\"future\":\"u8\"}")]
    [InlineData("{\"stream\":\"u8\"}")]
    [InlineData("{\"record\":{\"fields\":\"bad\"}}")]
    [InlineData("{\"record\":{\"fields\":[\"bad\"]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":1,\"type\":\"u8\"}]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":\"\",\"type\":\"u8\"}]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":\"field\"}]}}")]
    [InlineData("{\"tuple\":{\"types\":\"bad\"}}")]
    [InlineData("{\"result\":{\"ok\":[],\"err\":null}}")]
    [InlineData("{\"variant\":{\"cases\":\"bad\"}}")]
    [InlineData("{\"variant\":{\"cases\":[{\"name\":\"case\"}]}}")]
    [InlineData("{\"enum\":{\"cases\":\"bad\"}}")]
    [InlineData("{\"handle\":\"bad\"}")]
    [InlineData("{\"handle\":{\"share\":0}}")]
    [InlineData("{\"handle\":{\"own\":\"resource\"}}")]
    [InlineData("{\"handle\":{\"own\":99}}")]
    public void RejectsMalformedAnonymousIdentities(string json)
    {
        AssertContractFailure(() => Create().Format(
            Document(Type(0, null, json)), new WitTypeReference.Defined(0)));
    }

    [Fact]
    public void RejectsHandlesWhoseTargetsAreNotNamedResources()
    {
        AssertContractFailure(() => Create().Format(
            Document(Type(0, null, "\"resource\""), Type(1, null, """{"handle":{"own":0}}""")),
            new WitTypeReference.Defined(1)));
        AssertContractFailure(() => Create().Format(
            Document(Type(0, "value", "\"not-resource\"", 0), Type(1, null, """{"handle":{"own":0}}""")),
            new WitTypeReference.Defined(1)));
        AssertContractFailure(() => Create().Format(
            Document(Type(0, null, """{"list":"u8"}"""), Type(1, null, """{"handle":{"borrow":0}}""")),
            new WitTypeReference.Defined(1)));
    }

    private static IWitTypeIdentityFormatter Create() =>
        Assert.IsAssignableFrom<IWitTypeIdentityFormatter>(
            new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter()));

    private static WitDocument Document(params WitTypeDefinition[] types) =>
        DocumentWithInterfaces(
            [new(0, "api", "wasi:test@1.2.3", [], [])], types);

    private static WitDocument DocumentWithInterfaces(
        ImmutableArray<WitInterface> interfaces,
        params WitTypeDefinition[] types) => new([], interfaces, [], [.. types], "{}");

    private static WitTypeDefinition Type(int id, string? name, string json, int? owner = null)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), owner);
    }

    private static void AssertContractFailure(Action action)
    {
        var exception = Assert.Throws<CompilerException>(action);
        Assert.Equal("NW1009", exception.Diagnostic.Id);
    }

    private sealed record UnknownReference : WitTypeReference;
}
