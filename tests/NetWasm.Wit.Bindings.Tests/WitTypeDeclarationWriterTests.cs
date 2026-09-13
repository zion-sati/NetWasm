using System;
using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitTypeDeclarationWriterTests
{
    [Fact]
    public void EmitsEachReachableNamedTypeOnceInTypeOrder()
    {
        var document = CreateDocument();

#pragma warning disable CA1859 // This test verifies the type declaration writer contract.
        IWitTypeDeclarationWriter writer = new WitTypeDeclarationWriter(
            new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
            new CodeWriterFactory(),
            new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(
                new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())),
            new WitTypeDefinitionClassifier());
#pragma warning restore CA1859
        var first = writer.Generate(document, document.Worlds[0]);
        var second = writer.Generate(document, document.Worlds[0]);

        Assert.Equal(first, second);
        Assert.Contains("public readonly struct Point", first);
        Assert.Contains("public enum ChoiceTag", first);
        Assert.Contains("public enum Color", first);
        Assert.Contains("public enum Permissions : uint", first);
        Assert.DoesNotContain("public readonly struct PointAlias", first);
        Assert.Contains("public sealed class Connection : IDisposable", first);
        Assert.Equal(1, Count(first, "public readonly struct Point\n"));
    }

    [Fact]
    public void ExportedResourceUsesBorrowedRepresentationWithoutTableLookup()
    {
        var resource = Type(0, "counter", "\"resource\"");
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("counter", 0),
            []);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [],
            [new WitWorldItem("api", 0, null)]);
        var document = new WitDocument([], [@interface], [world], [resource], "{}");

        var source = new WitTypeDeclarationWriter(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()), new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())), new WitTypeDefinitionClassifier())
            .Generate(document, world);

        Assert.Contains(
            "var representation = _kind == 3 ? _handle : ExportRep(_handle);",
            source);
        Assert.Contains(
            "internal static Counter LiftExport(uint handle, bool ownsHandle)",
            source);
    }

    [Fact]
    public void ImportedResourceAliasUsesTheOriginalResourceDeclaration()
    {
        var resource = Type(0, "pollable", "\"resource\"");
        var alias = Type(1, "pollable", """{"type":0}""");
        var poll = new WitInterface(
            0,
            "poll",
            "wasi:io@0.2.11",
            ImmutableDictionary<string, int>.Empty.Add("pollable", 0),
            []);
        var clock = new WitInterface(
            1,
            "monotonic-clock",
            "wasi:clocks@0.2.11",
            ImmutableDictionary<string, int>.Empty.Add("pollable", 1),
            [new WitFunction(
                "subscribe",
                [],
                new WitTypeReference.Defined(1),
                new WitFunctionKind("freestanding"))]);
        var world = new WitWorld(
            0,
            "imports",
            "wasi:clocks@0.2.11",
            [
                new WitWorldItem("poll", 0, null),
                new WitWorldItem("monotonic-clock", 1, null),
            ],
            []);
        var document = new WitDocument(
            [],
            [poll, clock],
            [world],
            [resource, alias],
            "{}");

        var source = new WitTypeDeclarationWriter(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()), new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())), new WitTypeDefinitionClassifier())
            .Generate(document, world);
        var methods = WitBindingWriterFixture.Create().Generate(document, world);

        Assert.Equal(1, Count(source, "public sealed class Pollable"));
        Assert.DoesNotContain("public readonly struct Pollable", source);
        Assert.Contains("public static Pollable Subscribe()", methods);
    }

    [Theory]
    [InlineData("""{"type":"u8"}""")]
    [InlineData("""{"list":"u8"}""")]
    [InlineData("""{"option":"u8"}""")]
    [InlineData("""{"tuple":["u8","u16"]}""")]
    [InlineData("""{"result":{"ok":"u8","err":"string"}}""")]
    [InlineData("""{"type":{"list":"u8"}}""")]
    [InlineData("""{"type":{"option":"u8"}}""")]
    [InlineData("""{"type":{"tuple":["u8","string"]}}""")]
    [InlineData("""{"type":{"result":{"ok":"u8","err":"string"}}}""")]
    public void AcceptsTransparentAliasesAcrossReferenceFamilies(string aliasJson)
    {
        var document = CreateDocument();
        var world = document.Worlds[0];
        var syntax = new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier());
        var writer = new WitTypeDeclarationWriter(
            syntax,
            new CodeWriterFactory(),
            new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(syntax),
                new WitTypeDefinitionClassifier());
        var baseline = writer.Generate(document, world);
        const int aliasId = 1_000;
        var alias = Type(aliasId, "payload", aliasJson);
        var withAlias = document with
        {
            Types = document.Types.Add(alias)
        };

        Assert.Equal(baseline, writer.Generate(withAlias, world));
    }

    [Fact]
    public void EmitsWideFlagsAsWordBackedValueType()
    {
        var flags = Type(0, "permissions", FlagsJson(65));
        var medium = Type(1, "medium", FlagsJson(40));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty
                .Add("permissions", 0)
                .Add("medium", 1),
            []);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        var document = new WitDocument([], [@interface], [world], [flags, medium], "{}");

        var source = new WitTypeDeclarationWriter(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()), new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())), new WitTypeDefinitionClassifier())
            .Generate(document, world);

        Assert.Contains("public readonly struct Permissions : IEquatable<Permissions>", source);
        Assert.Contains("public enum Medium : ulong", source);
        Assert.Contains("internal static Permissions FromWords(uint[] words)", source);
        Assert.Contains("words[index] = operation == 0 ?", source);
        Assert.Contains("words[2] &= (1u << 1) - 1u;", source);
    }

    [Fact]
    public void RejectsUnsupportedNamedTypeKinds()
    {
        var definition = Type(0, "unsupported", "{\"future\":\"u32\"}");
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("unsupported", 0),
            []);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        var document = new WitDocument([], [@interface], [world], [definition], "{}");

        var exception = Assert.Throws<WitBindingException>(() =>
            new WitTypeDeclarationWriter(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()), new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())), new WitTypeDefinitionClassifier())
                .Generate(document, world));

        Assert.Contains("unsupported named WIT type", exception.Message,
            StringComparison.Ordinal);
    }

    private static WitDocument CreateDocument()
    {
        var types = ImmutableArray.Create(
            Type(0, "point", "{\"record\":{\"fields\":[{\"name\":\"x\",\"type\":\"s32\"},{\"name\":\"y\",\"type\":\"s32\"}]}}"),
            Type(1, "choice", "{\"variant\":{\"cases\":[{\"name\":\"none\",\"type\":null},{\"name\":\"point\",\"type\":0}]}}"),
            Type(2, "color", "{\"enum\":{\"cases\":[{\"name\":\"red\"},{\"name\":\"blue\"}]}}"),
            Type(3, "permissions", "{\"flags\":{\"flags\":[{\"name\":\"read\"},{\"name\":\"write\"}]}}"),
            Type(4, "point-alias", "{\"type\":0}"),
            Type(5, "connection", "\"resource\""));
        var typeMap = types.ToImmutableDictionary(type => type.Name!, type => type.Id);
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            typeMap,
            []);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null), new WitWorldItem("again", 0, null)],
            []);
        return new WitDocument([], [@interface], [world], types, "{}");
    }

    private static WitTypeDefinition Type(int id, string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), 0);
    }

    private static int Count(string value, string pattern)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += pattern.Length;
        }
        return count;
    }

    private static string FlagsJson(int count) =>
        "{\"flags\":{\"flags\":[" +
        string.Join(',', Enumerable.Range(0, count).Select(index =>
            $"{{\"name\":\"flag-{index}\"}}")) + "]}}";

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var definitions = new WitTypeDefinitionClassifier();
        var syntax = new WitBindingSyntaxFormatter(definitions);
        var aliases = new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(syntax);

        Assert.Throws<ArgumentNullException>(() =>
            new WitTypeDeclarationWriter(null!, new CodeWriterFactory(), aliases, definitions));
        Assert.Throws<ArgumentNullException>(() =>
            new WitTypeDeclarationWriter(syntax, null!, aliases, definitions));
        Assert.Throws<ArgumentNullException>(() =>
            new WitTypeDeclarationWriter(syntax, new CodeWriterFactory(), null!, definitions));
        Assert.Throws<ArgumentNullException>(() =>
            new WitTypeDeclarationWriter(syntax, new CodeWriterFactory(), aliases, null!));
    }
}
