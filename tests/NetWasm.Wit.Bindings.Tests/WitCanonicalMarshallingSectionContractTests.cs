using System.Collections.Immutable;
using System.Text.Json;

using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitCanonicalMarshallingSectionContractTests
{
    [Fact]
    public void TypeSectionRejectsMissingCapabilityDependencies()
    {
        var types = new WitCanonicalTypeResolver();
        var layouts = new CanonicalAbiMemoryLayoutPlanner();
        var reachability = new WitCanonicalTypeReachabilityResolver();
        var syntax = new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier());
        var writers = new CodeWriterFactory();

        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            null!, layouts, reachability, syntax, writers));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            types, null!, reachability, syntax, writers));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            types, layouts, null!, syntax, writers));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            types, layouts, reachability, null!, writers));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            types, layouts, reachability, syntax, null!));
    }

    [Fact]
    public void TypeSectionGeneratesReachableMarshallersThroughContract()
    {
        using var json = JsonDocument.Parse(
            "{\"record\":{\"fields\":[{\"name\":\"value\",\"type\":\"s32\"}]}}");
        var type = new WitTypeDefinition(0, "value", json.RootElement.Clone(), 0);
        var function = new WitFunction(
            "read",
            [],
            new WitTypeReference.Defined(0),
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("value", 0),
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        var document = new WitDocument([], [@interface], [world], [type], "{}");

        var section = AsSection(new WitCanonicalMarshallingTypeSectionWriter(
            new WitCanonicalTypeResolver(),
            new NetWasm.Compiler.Core.CanonicalAbiMemoryLayoutPlanner(),
            new WitCanonicalTypeReachabilityResolver(),
            new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
            new CodeWriterFactory()));

        var source = section.Generate(document, world);

        Assert.Contains("internal static CanonicalBuffer LowerType0", source);
        Assert.Contains("internal static Value LiftType0", source);
    }

    private static IWitCanonicalMarshallingTypeSectionWriter AsSection(object value) =>
        (IWitCanonicalMarshallingTypeSectionWriter)value;
}
