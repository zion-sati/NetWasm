using System;
using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitCSharpBindingGeneratorTests
{
    [Fact]
    public void GeneratesDeterministicSynchronousImportExportAndOwnedTypes()
    {
        var document = Document(
            new WitFunction(
                "transform-value",
                [new WitParameter("input-value", new WitTypeReference.Defined(0))],
                new WitTypeReference.Defined(1),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "run",
                [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
                new WitTypeReference.Primitive("u32"),
                new WitFunctionKind("freestanding")));
#pragma warning disable CA1859 // This test verifies the binding generator contract.
        IWitCSharpBindingGenerator generator = CreateGenerator();
#pragma warning restore CA1859

        var first = generator.Generate(document, document.Worlds[0]);
        var second = generator.Generate(document, document.Worlds[0]);
        var composed = WitBindingCompositionRoot.Create()
            .Generate(document, document.Worlds[0]);

        Assert.Equal(first, second);
        Assert.Equal(first, composed);
        Assert.Contains("namespace NetWasm.Wit.Example.Smoke._1._0._0;", first,
            StringComparison.Ordinal);
        Assert.Contains("public readonly struct Point", first, StringComparison.Ordinal);
        Assert.Contains("public static WitResult<Point, string> TransformValue(Point inputValue)",
            first, StringComparison.Ordinal);
        Assert.Contains("private static extern void __CanonicalExampleSmoke100Host_TransformValue(int __f0, nuint __result);",
            first, StringComparison.Ordinal);
        Assert.Contains("[WitExport(\"run\")]", first,
            StringComparison.Ordinal);
        Assert.Contains("[WitImport(\"example:smoke@1.0.0/host\", \"transform-value\")]",
            first, StringComparison.Ordinal);
        Assert.Contains("public static partial uint Run(uint value);", first,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAsyncFunctionsBeforeGeneratingPartialBindings()
    {
        var function = new WitFunction(
            "[async]run",
            [],
            null,
            new WitFunctionKind("async-freestanding"));
        var document = Document(function, function);

        var exception = Assert.Throws<CompilerException>(() =>
            CreateGenerator().Generate(document, document.Worlds[0]));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("asynchronous WIT function", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var valid = CreateCapabilities();

        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            null!, valid.TypeDeclarations, valid.BindingMethods, valid.Marshalling,
            valid.Syntax, valid.Writers));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            valid.Validator, null!, valid.BindingMethods, valid.Marshalling,
            valid.Syntax, valid.Writers));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            valid.Validator, valid.TypeDeclarations, null!, valid.Marshalling,
            valid.Syntax, valid.Writers));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            valid.Validator, valid.TypeDeclarations, valid.BindingMethods, null!,
            valid.Syntax, valid.Writers));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            valid.Validator, valid.TypeDeclarations, valid.BindingMethods, valid.Marshalling,
            null!, valid.Writers));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            valid.Validator, valid.TypeDeclarations, valid.BindingMethods, valid.Marshalling,
            valid.Syntax, null!));
    }

    [Fact]
    public void GeneratesDirectWorldImportsInCanonicalRootModule()
    {
        var function = new WitFunction(
            "ping",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("freestanding"));
        var document = Document(function, function);
        var world = document.Worlds[0] with
        {
            Imports = [new WitWorldItem("ping", null, function)],
        };

        var bindings = CreateGenerator().Generate(document, world);

        Assert.Contains("[WitImport(\"ping\")]", bindings,
            StringComparison.Ordinal);
    }

    // Every function the world imports directly is a member of one class,
    // which a class per function would declare again.
    [Fact]
    public void GeneratesTheWorldsDirectImportsInOneClass()
    {
        var ping = new WitFunction(
            "ping",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("freestanding"));
        var pong = ping with { Name = "pong" };
        var document = Document(ping, ping);
        var world = document.Worlds[0] with
        {
            Imports = [new WitWorldItem("ping", null, ping), new WitWorldItem("host", 0, null), new WitWorldItem("pong", null, pong)],
        };

        var bindings = CreateGenerator().Generate(document, world);

        Assert.Single(bindings.Split('\n'), line => line == "public static partial class SmokeImports");
        Assert.Contains("[WitImport(\"ping\")]", bindings, StringComparison.Ordinal);
        Assert.Contains("[WitImport(\"pong\")]", bindings, StringComparison.Ordinal);
        Assert.Contains("public static partial class HostImports", bindings, StringComparison.Ordinal);
    }

    // A type alias crosses as the type it names: an alias of u64, or an alias
    // of that alias, is the cast u64 is, not a copy through memory, whether a
    // world's own function or an interface's takes or returns it.
    [Fact]
    public void AnAliasCrossesAsTheTypeItNames()
    {
        var u64 = new WitTypeReference.Primitive("u64");
        var count = new WitTypeReference.Defined(2);
        var total = new WitTypeReference.Defined(3);
        static WitFunction Function(string name, WitTypeReference type) =>
            new(name, [new WitParameter("value", type)], type, new WitFunctionKind("freestanding"));
        var document = Document(Function("plain", u64), Function("run", u64));
        document = document with
        {
            Interfaces =
            [
                document.Interfaces[0] with
                {
                    Types = document.Interfaces[0].Types.Add("count", 2),
                    Functions = [Function("plain", u64), Function("counted", count)],
                },
            ],
            Types =
            [
                .. document.Types,
                new WitTypeDefinition(2, "count", Json("""{"type":"u64"}"""), 0),
                new WitTypeDefinition(3, "total", Json("""{"type":2}"""), null) { OwnerWorld = 0 },
            ],
        };
        var world = document.Worlds[0] with
        {
            Imports =
            [
                new WitWorldItem("host", 0, null),
                new WitWorldItem("direct", null, Function("direct", u64)),
                new WitWorldItem("aliased", null, Function("aliased", total)),
            ],
            Exports = [new WitWorldItem("run", null, Function("run", u64)), new WitWorldItem("served", null, Function("served", total))],
            Types = ImmutableDictionary<string, int>.Empty.Add("total", 3),
        };

        var bindings = CreateGenerator().Generate(document, world);

        Assert.DoesNotContain("__CanonicalFlat", bindings, StringComparison.Ordinal);
        Assert.DoesNotContain("__CanonicalMarshalling", bindings, StringComparison.Ordinal);
        Assert.Equal(Method(bindings, "Plain"), Method(bindings, "Counted").Replace("Counted", "Plain"));
        Assert.Equal(Method(bindings, "Direct"), Method(bindings, "Aliased").Replace("Aliased", "Direct"));
        Assert.Equal(Method(bindings, "__Canonical__Run"),
            Method(bindings, "__Canonical__Served").Replace("Served", "Run"));
    }

    // The text of the generated method with this name, up to its closing brace.
    private static string Method(string bindings, string name)
    {
        var start = bindings.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no method {name}");
        var end = bindings.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return bindings[start..end];
    }

    [Fact]
    public void UsesTheSelectedNamespace()
    {
        var function = new WitFunction("ping", [], null, new WitFunctionKind("freestanding"));
        var document = Document(function, function);

        var generator = CreateGenerator();

        Assert.Contains("\nnamespace My.Bindings;\n", generator.Generate(document, document.Worlds[0], "My.Bindings"),
            StringComparison.Ordinal);
        Assert.Contains("\nnamespace NetWasm.Wit.Example.Smoke._1._0._0;\n", generator.Generate(document, document.Worlds[0]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratesManagedStringAdaptersAndExportPostReturn()
    {
        var function = new WitFunction(
            "echo",
            [new WitParameter("value", new WitTypeReference.Primitive("string"))],
            new WitTypeReference.Primitive("string"),
            new WitFunctionKind("freestanding"));
        var document = Document(function, function);
        var world = document.Worlds[0] with
        {
            Imports = [new WitWorldItem("echo", null, function)],
            Exports = [new WitWorldItem("echo", null, function)],
        };

        var bindings = CreateGenerator().Generate(document, world);

        Assert.Contains("public static string Echo(string value)", bindings,
            StringComparison.Ordinal);
        Assert.Contains("private static extern void __Canonical__Echo(nuint __f0, nuint __f1, nuint __result);",
            bindings, StringComparison.Ordinal);
        Assert.Contains("[WitPostReturn(\"echo\")]", bindings,
            StringComparison.Ordinal);
        Assert.Contains("CanonicalAbi.Free(CanonicalAbi.ReadAddress(__result, 0));",
            bindings, StringComparison.Ordinal);
    }

    private static WitDocument Document(WitFunction import, WitFunction export)
    {
        var types = ImmutableArray.Create(
            new WitTypeDefinition(
                0,
                "point",
                Json("""{"record":{"fields":[{"name":"x","type":"s32"}]}}"""),
                0),
            new WitTypeDefinition(
                1,
                null,
                Json("""{"result":{"ok":0,"err":"string"}}"""),
                null));
        var @interface = new WitInterface(
            0,
            "host",
            "example:smoke@1.0.0",
            ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, int>("point", 0),
            }),
            [import]);
        var world = new WitWorld(
            0,
            "smoke",
            "example:smoke@1.0.0",
            [new WitWorldItem("host", 0, null)],
            [new WitWorldItem("run", null, export)]);
        return new WitDocument(
            [new WitPackage(0, "example:smoke@1.0.0",
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty)],
            [@interface],
            [world],
            types,
            "{}");
    }

    private static WitCSharpBindingGenerator CreateGenerator() =>
        new WitCSharpBindingGenerator(
            new WitWorldValidator(),
            new WitTypeDeclarationWriter(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()), new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())), new WitTypeDefinitionClassifier()),
            WitBindingWriterFixture.Create(),
            WitCanonicalMarshallingWriterFixture.Create(),
            new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
            new CodeWriterFactory());

    private static (
        IWitWorldValidator Validator,
        IWitTypeDeclarationWriter TypeDeclarations,
        IWitBindingMethodWriter BindingMethods,
        IWitCanonicalMarshallingWriter Marshalling,
        IWitBindingSyntaxFormatter Syntax,
        ICodeWriterFactory Writers) CreateCapabilities()
    {
        var definitions = new WitTypeDefinitionClassifier();
        var syntax = new WitBindingSyntaxFormatter(definitions);
        return (
            new WitWorldValidator(),
            new WitTypeDeclarationWriter(
                syntax,
                new CodeWriterFactory(),
                new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(syntax),
                definitions),
            WitBindingWriterFixture.Create(),
            WitCanonicalMarshallingWriterFixture.Create(),
            syntax,
            new CodeWriterFactory());
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
