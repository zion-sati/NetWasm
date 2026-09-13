using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentManifestBuilderTests
{
    [Fact]
    public void RecordsDeterministicJavaScriptBoundaryAndAdapterProvenance()
    {
        var world = new WitWorld(0, "application", "example:component@1.0.0", [], []);
        var document = new WitDocument(
            [new WitPackage(0, "example:component@1.0.0", [],
                ImmutableDictionary<string, int>.Empty.Add("application", 0))],
            [],
            [world],
            [],
            "{\"packages\":[]}");
        var inputs = new ComponentManifestInputs(
            new ComponentJavaScriptBoundary(
                [
                    new("z.module", "second", ["i32"], "i32", null),
                    new("a.module", "first", [], "void", "task"),
                ],
                [
                    new("z-export", [], "void", null),
                    new("a-export", ["i32"], "i32", "value-task"),
                ]),
            new ComponentAdapterVersions("1.28.1", "0.20.1"));

        var manifest = ((IComponentManifestBuilder)new ComponentManifestBuilder(
                new StubWasmTools()))
            .Build(document, world, ComponentTarget.Wasm32Wasi02, inputs);

        Assert.Equal(["a.module", "z.module"],
            manifest.JavaScript.Imports.Select(import => import.Module));
        Assert.Equal(["a-export", "z-export"],
            manifest.JavaScript.Exports.Select(export => export.Name));
        Assert.Equal("task", manifest.JavaScript.Imports[0].AsyncReturn);
        Assert.Equal("value-task", manifest.JavaScript.Exports[0].AsyncReturn);
        Assert.Equal("1.28.1", manifest.Adapters.Jco);
        Assert.Equal("0.20.1", manifest.Adapters.Preview2Shim);
        Assert.Equal("wasm-tools 1.256.0", manifest.WasmToolsVersion);
        Assert.Equal(1, manifest.Version);
        Assert.Equal("example:component@1.0.0", manifest.Package);
        Assert.Equal("application", manifest.World);
        Assert.Equal("wasm32", manifest.Target);
        Assert.Equal("0.2", manifest.WasiVersion);
        Assert.Equal("utf8", manifest.CanonicalStringEncoding);
        Assert.Equal(64, manifest.NormalizedWitSha256.Length);
        Assert.Empty(manifest.ImportedInterfaces);
        Assert.Empty(manifest.ExportedInterfaces);
        Assert.Empty(manifest.ImportedFunctions);
        Assert.Empty(manifest.ExportedFunctions);
        Assert.Equal(["i32"], manifest.JavaScript.Imports[1].Parameters);
        Assert.Equal("i32", manifest.JavaScript.Imports[1].Result);
        Assert.Empty(manifest.JavaScript.Exports[1].Parameters);
        Assert.Equal("void", manifest.JavaScript.Exports[1].Result);
    }

    [Theory]
    [InlineData("wasm32", "0.1", "unsupported WASI version")]
    [InlineData("wasm16", "0.2", "unsupported component target width")]
    public void RejectsUnsupportedComponentTargets(
        string width,
        string wasiVersion,
        string expectedMessage)
    {
        var document = new WitDocument([], [], [], [], "{}");
        var world = new WitWorld(0, "app", "example:test@1.0.0", [], []);
        var builder = new ComponentManifestBuilder(new StubWasmTools());

        var exception = Assert.Throws<CompilerException>(() => builder.Build(
            document,
            world,
            new ComponentTarget(width, wasiVersion, "utf8"),
            new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None)));

        Assert.Contains(expectedMessage, exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWasmToolsWithoutUsableVersion()
    {
        var document = new WitDocument([], [], [], [], "{}");
        var world = new WitWorld(0, "app", "example:test@1.0.0", [], []);

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentManifestBuilder(new FailingWasmTools())
                .Build(document, world, ComponentTarget.Wasm32Wasi02,
                    new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None)));

        Assert.Contains("usable version", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsSortedInterfacesAndFunctionsFromWorldItems()
    {
        var functions = ImmutableArray.Create(
            new WitFunction("zeta", [], null, new WitFunctionKind("freestanding")),
            new WitFunction("alpha", [], null, new WitFunctionKind("freestanding")));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty,
            functions);
        var directImport = new WitWorldItem(
            "notify",
            null,
            new WitFunction("notify", [], null, new WitFunctionKind("freestanding")));
        var directExport = new WitWorldItem(
            "run",
            null,
            new WitFunction("run", [], null, new WitFunctionKind("freestanding")));
        var world = new WitWorld(
            0,
            "app",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null), directImport],
            [new WitWorldItem("api", 0, null), directExport]);
        var document = new WitDocument([], [@interface], [world], [], "{}");

        var manifest = new ComponentManifestBuilder(new StubWasmTools())
            .Build(document, world, ComponentTarget.Wasm32Wasi02,
                new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None));

        Assert.Equal(["example:test@1.0.0/api"], manifest.ImportedInterfaces);
        Assert.Equal(["example:test@1.0.0/api"], manifest.ExportedInterfaces);
        Assert.Equal(["example:test@1.0.0/api#alpha", "example:test@1.0.0/api#zeta", "notify"], manifest.ImportedFunctions);
        Assert.Equal(["example:test@1.0.0/api#alpha", "example:test@1.0.0/api#zeta", "run"], manifest.ExportedFunctions);
    }

    [Fact]
    public void ComponentContractRecordsExposeAllBoundaryMetadata()
    {
        var target = ComponentTarget.Wasm64Wasi02;
        var package = new WitPackage(
            2,
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("api", 3),
            ImmutableDictionary<string, int>.Empty);
        var item = new WitWorldItem("api", 3, null);
        var @interface = new WitInterface(3, "api", package.Name,
            package.Interfaces, []);
        var world = new WitWorld(4, "world", package.Name, [item], []);
        var document = new WitDocument([package], [@interface], [world], [], "{}");
        var function = new WitFunction("run", [], null,
            new WitFunctionKind("freestanding"));
        var javascript = new ComponentJavaScriptBoundary(
            [new ComponentJavaScriptImport("module", "run", [], "void", null)],
            [new ComponentJavaScriptExport("run", [], "void", null)]);
        var inputs = new ComponentManifestInputs(javascript,
            new ComponentAdapterVersions("jco", "shim"));

        Assert.Equal("wasm64", target.Width);
        Assert.Equal(2, package.Id);
        Assert.Equal(3, @interface.Id);
        Assert.Equal("api", item.Name);
        Assert.Equal(4, world.Id);
        Assert.Single(document.Packages);
        Assert.Equal("module", inputs.JavaScript.Imports[0].Module);
        Assert.Equal("run", inputs.JavaScript.Imports[0].Name);
        Assert.Null(inputs.JavaScript.Imports[0].AsyncReturn);
        Assert.Equal("jco", inputs.Adapters.Jco);
        Assert.Equal("shim", inputs.Adapters.Preview2Shim);
        Assert.Equal("run", function.Name);
        Assert.Empty(ComponentJavaScriptBoundary.Empty.Imports);
        Assert.Null(ComponentAdapterVersions.None.Jco);
    }

    private sealed class StubWasmTools : IWasmTools
    {
        public ToolResult Run(params IEnumerable<string> arguments) =>
            new(0, "wasm-tools 1.256.0\n", "");
    }

    private sealed class FailingWasmTools : IWasmTools
    {
        public ToolResult Run(params IEnumerable<string> arguments) =>
            new(1, string.Empty, "tool unavailable");
    }
}
