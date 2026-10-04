using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Catalogs;

public sealed class WitWorkerTypeProjectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RequiresCollaborators(int missing)
    {
        Assert.Throws<ArgumentNullException>(() => new WitWorkerTypeProjector(
            missing == 0 ? null! : References(),
            missing == 1 ? null! : new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter()),
            missing == 2 ? null! : new WitInterfaceSpecifierFormatter()));
    }

    [Fact]
    public void ProjectsSparseTransitiveCrossInterfaceTypesWithoutNameCollisions()
    {
        var document = Document([
            Type(0, "unused", "{\"type\":\"string\"}", 0),
            Type(1, "item", "{\"record\":{\"fields\":[{\"name\":\"value\",\"type\":\"s64\"}]}}", 0),
            Type(2, "item", "{\"type\":1}", 1),
            Type(3, null, "{\"list\":2}"),
            Type(4, "envelope", "{\"record\":{\"fields\":[{\"name\":\"items\",\"type\":3},{\"name\":\"same\",\"type\":2}]}}", 1),
        ]);
        var projected = Create().Project(document, [Export(new("defined", null, 4))]);

        Assert.Equal([1, 2, 3, 4], projected.Select(type => type.Id));
        Assert.Equal("item", projected[0].Name);
        Assert.Equal("item", projected[1].Name);
        Assert.Equal(new("interface", "example:types/first@1.0.0"), projected[0].Owner);
        Assert.Equal(new("interface", "other:types/second@2.0.0"), projected[1].Owner);
        Assert.Null(projected[2].Owner);
        Assert.Equal(1, projected[1].Kind.GetProperty("type").GetInt32());
        Assert.Equal(3, projected[3].Kind.GetProperty("record").GetProperty("fields")[0].GetProperty("type").GetInt32());
        Assert.Equal(projected.Select(type => type.Kind.GetRawText()),
            Create().Project(document, [Export(new("defined", null, 4))]).Select(type => type.Kind.GetRawText()));
    }

    [Fact]
    public void IncludesParameterAndResourceKindRoots()
    {
        var document = Document([
            Type(0, "token", "\"resource\"", 0),
            Type(1, null, "{\"handle\":{\"borrow\":0}}"),
        ]);
        var export = Export(null) with
        {
            Parameters = [new("value", new("defined", null, 1)), new("count", new("primitive", "u32", null))],
            Kind = new("method", 0),
        };

        Assert.Equal([0, 1], Create().Project(document, [export]).Select(type => type.Id));
        Assert.Empty(Create().Project(document, []));
        Assert.Empty(Create().Project(document, [Export(new("primitive", "string", null))]));
    }

    [Fact]
    public void RequiresExplicitInventoriesAndDocument()
    {
        var projector = Create();
        var document = Document([]);
        Assert.Throws<ArgumentNullException>(() => projector.Project(null!, []));
        Invalid(() => projector.Project(document with { Types = default }, []), "explicit inventories");
        Invalid(() => projector.Project(document with { Interfaces = default }, []), "explicit inventories");
        Invalid(() => projector.Project(document, default), "explicit inventories");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void RejectsUnknownRootReferences(int id) =>
        Invalid(() => Create().Project(Document([Type(0, null, "{\"type\":\"u32\"}")]),
            [Export(new("defined", null, id))]), "unknown type");

    [Fact]
    public void RejectsMissingAndMismatchedDefinitions()
    {
        Invalid(() => Create().Project(Document([null!]), [Export(new("defined", null, 0))]), "unknown type");
        Invalid(() => Create().Project(Document([Type(1, null, "{\"type\":\"u32\"}")]),
            [Export(new("defined", null, 0))]), "unknown type");
        Invalid(() => Create().Project(Document([Type(0, null, "{\"list\":1}")]),
            [Export(new("defined", null, 0))]), "unknown type");
    }

    [Fact]
    public void RejectsAliasAndValueCyclesBeforeCanonicalResolution()
    {
        Invalid(() => Create().Project(Document([
            Type(0, null, "{\"type\":1}"), Type(1, null, "{\"type\":0}"),
        ]), [Export(new("defined", null, 0))]), "cycle");
        Invalid(() => Create().Project(Document([Type(0, null, "{\"list\":0}")]),
            [Export(new("defined", null, 0))]), "cycle");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void RejectsUnknownOwners(int owner) =>
        Invalid(() => Create().Project(Document([Type(0, "item", "{\"type\":\"u32\"}", owner)]),
            [Export(new("defined", null, 0))]), "unknown owner");

    [Fact]
    public void RejectsMissingOwnerAndUnsupportedWorldOwnedNames()
    {
        var document = Document([Type(0, "item", "{\"type\":\"u32\"}", 0)]) with { Interfaces = [null!] };
        Invalid(() => Create().Project(document, [Export(new("defined", null, 0))]), "unknown owner");
        Invalid(() => Create().Project(Document([Type(0, "item", "{\"type\":\"u32\"}")]),
            [Export(new("defined", null, 0))]), "supported interface owner");
    }

    [Theory]
    [InlineData("{\"tuple\":{}}")]
    [InlineData("{\"enum\":{}}")]
    [InlineData("{\"flags\":{\"flags\":1}}")]
    [InlineData("{\"handle\":{\"own\":1.5}}")]
    public void RejectsMalformedDefinitions(string json) =>
        Assert.Throws<CompilerException>(() => Create().Project(Document([Type(0, null, json)]),
            [Export(new("defined", null, 0))]));

    [Theory]
    [InlineData("bogus", null, null)]
    [InlineData("primitive", "string", 0)]
    [InlineData("primitive", null, null)]
    [InlineData("defined", "u32", 0)]
    [InlineData("defined", null, null)]
    public void RejectsInvalidExportReferences(string kind, string? primitive, int? definition) =>
        Invalid(() => Create().Project(Document([]), [Export(new(kind, primitive, definition))]), "reference is invalid");

    [Fact]
    public void RejectsUnsupportedPrimitiveAndKind()
    {
        Invalid(() => Create().Project(Document([]), [Export(new("primitive", "unknown", null))]), "primitive");
        Invalid(() => Create().Project(Document([Type(0, null, "{\"future\":\"string\"}")]),
            [Export(new("defined", null, 0))]), "unsupported");
    }

    [Fact]
    public void ProjectsRepeatedReferenceDagOncePerDefinition()
    {
        var types = ImmutableArray.CreateBuilder<WitTypeDefinition>();
        types.Add(Type(0, null, "{\"type\":\"u8\"}"));
        for (var i = 1; i <= 40; i++)
        {
            var child = (i - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            types.Add(Type(i, null, "{\"tuple\":{\"types\":[" + child + "," + child + "]}}"));
        }
        var references = new CountingReferences(References());
        var projector = new WitWorkerTypeProjector(references,
            new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter()), new WitInterfaceSpecifierFormatter());

        var projected = projector.Project(Document(types.ToImmutable()), [Export(new("defined", null, 40))]);

        Assert.Equal(Enumerable.Range(0, 41), projected.Select(type => type.Id));
        Assert.Equal(41, references.Count);
    }

    [Fact]
    public void AcceptsResourceAliasesAndSharedTargetsButRejectsOtherTargets()
    {
        var document = Document([
            Type(0, "token", "\"resource\"", 0),
            Type(1, "alias", "{\"type\":0}", 1),
            Type(2, null, "{\"handle\":{\"own\":1}}"),
            Type(3, null, "{\"handle\":{\"borrow\":1}}"),
            Type(4, null, "{\"tuple\":{\"types\":[2,3]}}"),
        ]);
        Assert.Equal([0, 1, 2, 3, 4], Create().Project(document,
            [Export(new("defined", null, 4))]).Select(type => type.Id));
        foreach (var kind in new[] { "{\"type\":\"u32\"}", "{\"record\":{\"fields\":[]}}" })
        {
            var wrong = Document([Type(0, null, kind), Type(1, null, "{\"handle\":{\"own\":0}}")]);
            Invalid(() => Create().Project(wrong, [Export(new("defined", null, 1))]), "resource type");
        }
    }

    [Fact]
    public void RejectsInvalidOrDuplicateNamedIdentitiesWithinOneOwner()
    {
        Invalid(() => Create().Project(Document([Type(0, " ", "{\"type\":\"u8\"}", 0)]),
            [Export(new("defined", null, 0))]), "named type identity");
        Invalid(() => Create().Project(Document([
            Type(0, "item", "{\"type\":\"u8\"}", 0), Type(1, "item", "{\"type\":0}", 0),
        ]), [Export(new("defined", null, 1))]), "duplicated");
    }

    private sealed class CountingReferences(IWitWorkerTypeReferenceReader inner) : IWitWorkerTypeReferenceReader
    {
        public int Count { get; private set; }
        public ImmutableArray<WitTypeReference> Read(JsonElement kind)
        {
            Count++;
            return inner.Read(kind);
        }
    }

    private static WitWorkerTypeProjector Create() => new WitWorkerTypeProjector(
        References(), new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter()), new WitInterfaceSpecifierFormatter());

    private static WitWorkerTypeReferenceReader References() =>
        new WitWorkerTypeReferenceReader(WitWorkerReferenceComposition.CreateReaders());

    private static WitDocument Document(ImmutableArray<WitTypeDefinition> types) => new([], [
        new(0, "first", "example:types@1.0.0", [], []),
        new(1, "second", "other:types@2.0.0", [], []),
    ], [], types, "{}");

    private static WitWorkerExport Export(WitWorkerTypeReference? result) => new(
        "example:types/first@1.0.0/read", "interface", "first", "example:types/first@1.0.0",
        "first", "read", "read", [], result, new("freestanding"));

    private static WitTypeDefinition Type(int id, string? name, string json, int? owner = null)
    {
        using var parsed = JsonDocument.Parse(json);
        return new(id, name, parsed.RootElement.Clone(), owner);
    }

    private static void Invalid(Action action, string message) =>
        Assert.Contains(message, Assert.Throws<CompilerException>(action).Diagnostic.Message, StringComparison.Ordinal);
}
