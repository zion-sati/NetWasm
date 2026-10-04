using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WitCoreBindingResolverTests
{
    [Fact]
    public void ConstructorRejectsMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new WitCoreBindingResolver(
            null!,
            new WitWorldSpecifierFormatter()));
        Assert.Throws<ArgumentNullException>(() => new WitCoreBindingResolver(
            new RecordingTools(0, "(module)", string.Empty),
            null!));
    }

    [Theory]
    [InlineData("interface-0", "example:worker/math-api@1",
        "example:worker/math-api@1")]
    [InlineData("math-api", "math-api", "math-api")]
    [InlineData("math-alias", "math-alias", "math-alias")]
    [InlineData("interface-0", "interface-0", "interface-0")]
    public void ResolvesQualifiedAndNamedWorldBindingsFromTheCoreInventory(
        string worldItemName,
        string physicalName,
        string expected)
    {
        var (document, world) = FunctionDocument(worldItemName);
        var tools = new RecordingTools(
            0,
            $"""
            (module
              (export "cm32p2|{physicalName}|add-value" (func 0))
              (export "cm32p2|{physicalName}|add-value_post" (func 1))
            )
            """,
            string.Empty);
        var resolver = new WitCoreBindingResolver(
            tools,
            new WitWorldSpecifierFormatter());

        var resolved = resolver.Resolve("fixture.wit", document, world);

        Assert.Equal(expected,
            Assert.Single(resolved.SelectWorld("application").Exports).CoreBindingName);
        Assert.Equal(
            ["component", "embed", "fixture.wit", "--world",
                "example:worker/application@1.0.0", "--dummy", "-t"],
            Assert.Single(tools.Invocations));
    }

    [Fact]
    public void ResolvesResourceOnlyAliasesFromExportResourceImports()
    {
        using var resourceKind = JsonDocument.Parse("\"resource\"");
        var definition = new WitInterface(
            0,
            "objects",
            "example:worker@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("widget", 0),
            []);
        var world = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new WitWorldItem("object-api", 0, null)]);
        var document = Document(
            definition,
            world,
            [new WitTypeDefinition(
                0, "widget", resourceKind.RootElement.Clone(), 0)]);
        var resolver = new WitCoreBindingResolver(
            new RecordingTools(
                0,
                """
                (module
                  (import "cm32p2|_ex_object-api" "widget_new" (func))
                  (import "cm32p2|_ex_object-api" "widget_rep" (func))
                  (import "cm32p2|_ex_object-api" "widget_drop" (func))
                  (export "cm32p2|object-api|widget_dtor" (func 0))
                )
                """,
                string.Empty),
            new WitWorldSpecifierFormatter());

        var resolved = resolver.Resolve("fixture.wit", document, world);

        Assert.Equal("object-api",
            Assert.Single(resolved.SelectWorld("application").Exports).CoreBindingName);
    }

    [Fact]
    public void KeepsImportAndExportNamespaceInventoriesSeparate()
    {
        var function = new WitFunction(
            "run", [], null, new WitFunctionKind("freestanding"));
        var package = "example:worker@1.0.0";
        var input = new WitInterface(0, "input", package, [], [function]);
        var output = new WitInterface(1, "output", package, [], [function]);
        var world = new WitWorld(
            0,
            "application",
            package,
            [new WitWorldItem("interface-0", 0, null)],
            [new WitWorldItem("interface-0", 1, null)]);
        var document = new WitDocument(
            [new WitPackage(
                0,
                package,
                ImmutableDictionary<string, int>.Empty
                    .Add("input", 0)
                    .Add("output", 1),
                ImmutableDictionary<string, int>.Empty.Add("application", 0))],
            [input, output],
            [world],
            [],
            "{}");
        var resolver = new WitCoreBindingResolver(
            new RecordingTools(
                0,
                """
                (module
                  (import "cm32p2|example:worker/input@1" "run" (func))
                  (export "cm32p2|interface-0|run" (func 0))
                  (export "cm32p2|interface-0|run_post" (func 1))
                )
                """,
                string.Empty),
            new WitWorldSpecifierFormatter());

        var resolved = resolver.Resolve("fixture.wit", document, world)
            .SelectWorld("application");

        Assert.Equal("example:worker/input@1",
            Assert.Single(resolved.Imports).CoreBindingName);
        Assert.Equal("interface-0",
            Assert.Single(resolved.Exports).CoreBindingName);
    }

    [Fact]
    public void MatchesMembersWhenASyntheticNameCollidesWithAnotherAlias()
    {
        var package = "example:worker@1.0.0";
        var math = new WitInterface(
            0,
            "math-api",
            package,
            [],
            [new("add-value", [], new WitTypeReference.Primitive("s32"), new("freestanding"))]);
        var logger = new WitInterface(
            1,
            "logger",
            package,
            [],
            [new("log", [], null, new("freestanding"))]);
        var world = new WitWorld(
            0,
            "application",
            package,
            [],
            [new("interface-0", 0, null), new("interface-0", 1, null)]);
        var document = new WitDocument(
            [],
            [math, logger],
            [world],
            [],
            "{}");

        var resolved = WitCoreBindingResolver.Resolve(
            document,
            world,
            """
            (module
              (export "cm32p2|example:worker/math-api@1|add-value" (func 0))
              (export "cm32p2|example:worker/math-api@1|add-value_post" (func 1))
              (export "cm32p2|interface-0|log" (func 2))
              (export "cm32p2|interface-0|log_post" (func 3))
            )
            """).SelectWorld("application");

        Assert.Equal("example:worker/math-api@1", resolved.Exports[0].CoreBindingName);
        Assert.Equal("interface-0", resolved.Exports[1].CoreBindingName);
    }

    [Fact]
    public void RejectsMemberCompatibleNamespaceAmbiguity()
    {
        var (document, world) = FunctionDocument("math-alias");

        var failure = Assert.Throws<CompilerException>(() =>
            WitCoreBindingResolver.Resolve(
                document,
                world,
                """
                (module
                  (export "cm32p2|math-alias|add-value" (func 0))
                  (export "cm32p2|math-alias|add-value_post" (func 1))
                  (export "cm32p2|example:worker/math-api@1|add-value" (func 2))
                  (export "cm32p2|example:worker/math-api@1|add-value_post" (func 3))
                )
                """));

        Assert.Contains("ambiguous core binding namespaces", failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTruncatedMemberInventories()
    {
        var (document, world) = FunctionDocument("math-alias");

        var failure = Assert.Throws<CompilerException>(() =>
            WitCoreBindingResolver.Resolve(
                document,
                world,
                """
                (module
                  (export "cm32p2|math-alias|add-value" (func 0))
                )
                """));

        Assert.Contains("no exact core binding namespace and member inventory",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMultiplePlacementsOfOneSemanticInterface()
    {
        var (document, world) = FunctionDocument(
            "first",
            new WitWorldItem("second", 0, null));
        var resolver = new WitCoreBindingResolver(
            new RecordingTools(
                0,
                """
                (module
                  (export "cm32p2|first|add-value" (func 0))
                  (export "cm32p2|second|add-value" (func 1))
                )
                """,
                string.Empty),
            new WitWorldSpecifierFormatter());

        var exception = Assert.Throws<CompilerException>(() =>
            resolver.Resolve("fixture.wit", document, world));

        Assert.Contains("multiple world placements", exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingOrFailedCoreInventories()
    {
        var (document, world) = FunctionDocument("math-alias");
        var missing = new WitCoreBindingResolver(
            new RecordingTools(0, "(module)", string.Empty),
            new WitWorldSpecifierFormatter());
        var missingFailure = Assert.Throws<CompilerException>(() =>
            missing.Resolve("fixture.wit", document, world));
        Assert.Contains("no exact core binding namespace", missingFailure.Message,
            StringComparison.Ordinal);

        var failed = new WitCoreBindingResolver(
            new RecordingTools(1, string.Empty, "bad\nworld"),
            new WitWorldSpecifierFormatter());
        var toolFailure = Assert.Throws<CompilerException>(() =>
            failed.Resolve("fixture.wit", document, world));
        Assert.Contains("invalid WIT core binding inventory: bad world",
            toolFailure.Message, StringComparison.Ordinal);

        var unknown = new WitCoreBindingResolver(
            new RecordingTools(1, string.Empty, " \r\n"),
            new WitWorldSpecifierFormatter());
        var unknownFailure = Assert.Throws<CompilerException>(() =>
            unknown.Resolve("fixture.wit", document, world));
        Assert.Contains("wasm-tools reported an unknown error", unknownFailure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAWorldThatDoesNotBelongToTheDocument()
    {
        var (document, world) = FunctionDocument("math-alias");
        var detached = world with { Id = 99, Name = "detached" };

        var failure = Assert.Throws<CompilerException>(() =>
            WitCoreBindingResolver.Resolve(
                document,
                detached,
                """
                (module
                  (export "cm32p2|math-alias|add-value" (func 0))
                  (export "cm32p2|math-alias|add-value_post" (func 1))
                )
                """));

        Assert.Contains("does not belong", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InterfaceWithoutCoreMembersUsesItsQualifiedName()
    {
        var definition = new WitInterface(
            0,
            "empty",
            "example:worker@1.0.0",
            [],
            []);
        var world = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new("empty-alias", 0, null)]);
        var document = Document(definition, world, []);

        var resolved = WitCoreBindingResolver.Resolve(document, world, "(module)");

        Assert.Equal(
            "example:worker/empty@1",
            Assert.Single(resolved.SelectWorld("application").Exports).CoreBindingName);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("(import \"cm32p2|math-alias\"")]
    [InlineData("(import \"cm32p2|math-alias\" \"add-value")]
    [InlineData("(export \"cm32p2|math-alias|add-value")]
    public void RejectsEmptyOrMalformedToolInventories(string inventory)
    {
        var (document, world) = FunctionDocument("math-alias");

        Assert.Throws<CompilerException>(() =>
            WitCoreBindingResolver.Resolve(document, world, inventory));
    }

    [Fact]
    public void IgnoresUnrelatedAndIncompleteCoreNamespaces()
    {
        var (document, world) = FunctionDocument("math-alias");

        var resolved = WitCoreBindingResolver.Resolve(
            document,
            world,
            """
            (module
              (import "other" "ignored" (func))
              (import "cm32p2" "ignored" (func))
              (import "cm32p2|" "ignored" (func))
              (export "other|ignored" (func 0))
              (export "cm32p2|missing-separator" (func 1))
              (export "cm32p2||missing-namespace" (func 2))
              (export "cm32p2|missing-member|" (func 3))
              (export "cm32p2|math-alias|add-value" (func 4))
              (export "cm32p2|math-alias|add-value_post" (func 5))
            )
            """);

        Assert.Equal(
            "math-alias",
            Assert.Single(resolved.SelectWorld("application").Exports).CoreBindingName);
    }

    [Fact]
    public void RootFunctionsAndNonResourceTypesNeedNoCoreNamespace()
    {
        using var scalarKind = JsonDocument.Parse("\"u32\"");
        using var recordKind = JsonDocument.Parse("{\"record\":{\"fields\":[]}}");
        var definition = new WitInterface(
            0,
            "empty",
            "example:worker@1.0.0",
            ImmutableDictionary<string, int>.Empty
                .Add("value", 0)
                .Add("record", 1),
            []);
        var world = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [new("configure", null, new("configure", [], null, new("freestanding")))],
            [new("empty-alias", 0, null)]);
        var document = Document(
            definition,
            world,
            [
                new(0, "value", scalarKind.RootElement.Clone(), 0),
                new(1, "record", recordKind.RootElement.Clone(), 0),
            ]);

        var resolved = WitCoreBindingResolver.Resolve(document, world, "(module)")
            .SelectWorld("application");

        Assert.Equal(string.Empty, Assert.Single(resolved.Imports).CoreBindingName);
        Assert.Equal("example:worker/empty@1",
            Assert.Single(resolved.Exports).CoreBindingName);
    }

    [Fact]
    public void ResolvesImportedResourceDropNamespace()
    {
        using var resourceKind = JsonDocument.Parse("\"resource\"");
        var definition = new WitInterface(
            0,
            "objects",
            "example:worker@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("widget", 0),
            []);
        var world = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [new("object-api", 0, null)],
            []);
        var document = Document(
            definition,
            world,
            [new(0, "widget", resourceKind.RootElement.Clone(), 0)]);

        var resolved = WitCoreBindingResolver.Resolve(
            document,
            world,
            """
            (module
              (import "cm32p2|object-api" "widget_drop" (func))
            )
            """)
            .SelectWorld("application");

        Assert.Equal("object-api", Assert.Single(resolved.Imports).CoreBindingName);
    }

    private static (WitDocument Document, WitWorld World) FunctionDocument(
        string worldItemName,
        params WitWorldItem[] additionalExports)
    {
        var function = new WitFunction(
            "add-value",
            [],
            new WitTypeReference.Primitive("s32"),
            new WitFunctionKind("freestanding"));
        var definition = new WitInterface(
            0,
            "math-api",
            "example:worker@1.0.0",
            ImmutableDictionary<string, int>.Empty,
            [function]);
        var world = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new WitWorldItem(worldItemName, 0, null), .. additionalExports]);
        return (Document(definition, world, []), world);
    }

    private static WitDocument Document(
        WitInterface definition,
        WitWorld world,
        ImmutableArray<WitTypeDefinition> types) => new(
        [new WitPackage(
            0,
            "example:worker@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add(definition.Name, 0),
            ImmutableDictionary<string, int>.Empty.Add(world.Name, 0))],
        [definition],
        [world],
        types,
        "{}");

    private sealed class RecordingTools(
        int exitCode,
        string output,
        string error) : IWasmTools
    {
        public List<string[]> Invocations { get; } = [];

        public ToolResult Run(params IEnumerable<string> arguments)
        {
            Invocations.Add(arguments.ToArray());
            return new(exitCode, output, error);
        }
    }
}
