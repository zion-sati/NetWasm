using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Wit.Bindings.FlatSlots;
using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingSectionContractTests
{
    [Fact]
    public void SectionWritersRejectMissingCapabilityDependencies()
    {
        var types = new WitCanonicalTypeResolver();
        var flattener = new CanonicalAbiTypeFlattener();
        var signatures = new CanonicalAbiSignaturePlanner(flattener);
        var layouts = new CanonicalAbiMemoryLayoutPlanner();
        var models = new WitBindingFunctionModelBuilder(new WitCanonicalFunctionBuilder(types), signatures);
        var syntax = new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier());
        var slotCoercions = new WitFlatSlotCoercionFormatter();
        var writers = new CodeWriterFactory();

        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            null!, flattener, layouts, models, syntax, slotCoercions, writers));
        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            types, null!, layouts, models, syntax, slotCoercions, writers));
        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            types, flattener, null!, models, syntax, slotCoercions, writers));
        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            types, flattener, layouts, null!, syntax, slotCoercions, writers));
        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            types, flattener, layouts, models, null!, slotCoercions, writers));
        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            types, flattener, layouts, models, syntax, null!, writers));

        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            types, flattener, layouts, models, syntax, slotCoercions, null!));
        Assert.Throws<ArgumentNullException>(() => new WitBindingExportSectionWriter(
            types, flattener, layouts, models, syntax, slotCoercions, null!));
        Assert.Throws<ArgumentNullException>(() => new WitBindingFlatSectionWriter(
            types, flattener, layouts, models, syntax, slotCoercions, null!));
    }

    [Fact]
    public void ImportAndExportSectionsGenerateIndependentlyThroughContracts()
    {
        var function = new WitFunction(
            "ping",
            [],
            null,
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
            [new WitWorldItem("api", 0, null)]);
        var document = new WitDocument([], [@interface], [world], [], "{}");

        var imports = AsImports(WitBindingWriterFixture.CreateImports())
            .Generate(document, world);
        var exports = AsExports(WitBindingWriterFixture.CreateExports())
            .Generate(document, world);

        Assert.Contains("public static class ApiImports", imports.Source);
        Assert.Contains("public static partial class TestExports", exports.Source);
        Assert.Contains("Ping();", exports.Source);
        Assert.DoesNotContain("ExampleTest100Api_Ping();", exports.Source);
        Assert.Empty(imports.LiftTypes);
        Assert.Empty(exports.LowerTypes);
    }

    [Fact]
    public void DirectWorldStringExportCallsItsDeclaredPartialMethodThroughExportContract()
    {
        var function = new WitFunction(
            "echo-back",
            [new WitParameter("value", new WitTypeReference.Primitive("string"))],
            new WitTypeReference.Primitive("string"),
            new WitFunctionKind("freestanding"));
        var world = new WitWorld(
            0,
            "strings",
            "example:strings@1.0.0",
            [],
            [new WitWorldItem(function.Name, null, function)]);
        var document = new WitDocument([], [], [world], [], "{}");

        var source = WitBindingWriterFixture.CreateExports().Generate(document, world).Source;

        Assert.Contains("public static partial string EchoBack(string value);", source);
        Assert.Contains("var __managedResult = EchoBack(__managed0);", source);
        Assert.DoesNotContain("__EchoBack(__managed0)", source);
    }

    [Fact]
    public void DirectWorldCompositeExportCallsItsDeclaredPartialMethodThroughExportContract()
    {
        var type = new WitTypeDefinition(
            0,
            "pair",
            Json("{\"record\":{\"fields\":[{\"name\":\"value\",\"type\":\"s32\"}]}}"),
            null);
        var function = new WitFunction(
            "round-trip",
            [new WitParameter("value", new WitTypeReference.Defined(type.Id))],
            new WitTypeReference.Defined(type.Id),
            new WitFunctionKind("freestanding"));
        var world = new WitWorld(
            0,
            "composite",
            "example:composite@1.0.0",
            [],
            [new WitWorldItem(function.Name, null, function)]);
        var document = new WitDocument([], [], [world], [type], "{}");

        var source = WitBindingWriterFixture.CreateExports().Generate(document, world).Source;

        Assert.Contains("public static partial Pair RoundTrip(Pair value);", source);
        Assert.Contains("var __managedResult = RoundTrip(__managed0);", source);
        Assert.DoesNotContain("__RoundTrip(__managed0)", source);
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static IWitBindingImportSectionWriter AsImports(object value) =>
        (IWitBindingImportSectionWriter)value;

    private static IWitBindingExportSectionWriter AsExports(object value) =>
        (IWitBindingExportSectionWriter)value;
}
