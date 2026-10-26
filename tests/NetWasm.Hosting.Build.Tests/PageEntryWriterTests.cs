using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed partial class PageEntryWriterTests
{
    private static readonly ImmutableArray<WorkerJavaScriptExport> Exports =
    [
        new("Add", ["i32", "i32"], "i32", null),
        new("FetchTitle", ["string"], "string", "task"),
        new("Read", [], "i64", "value-task"),
        new("Echo", ["bytes", "char"], "bytes", null),
        new("Reset", [], "void", null),
        new("Scale", ["i8[]", "i16[]", "u16[]", "i32[]", "u32[]"], "f64[]", null),
        new("Widen", ["i64[]", "u64[]", "f32[]"], "f64[]", "task"),
    ];

    // A JSObject stays in the page realm, so only the page host can publish it.
    private static readonly WorkerJavaScriptExport PageOnlyExport = new("Wrap", ["object"], "object", null)
    {
        ParameterNames = ["element"],
    };

    [Fact]
    public void WritesThePageHostEntryWithBundledModulesAndConfiguration()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "page-entry.mjs");
        HostingCatalogFixture.Write(Path.Combine(directory.Path, "hosting"));
        var writer = Assert.IsAssignableFrom<IPageEntryWriter>(new PageEntryWriter());

        writer.WriteHost(HostRequest(directory.Path, output));

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var source = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r", source, StringComparison.Ordinal);
        Assert.Contains("import { createPageSession } from \"file://", source, StringComparison.Ordinal);
        Assert.Contains("/hosting/page-session-root.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("import { createSelectedPreview2ShimFactory } from", source, StringComparison.Ordinal);
        Assert.Contains("/preview2-shim/dist/browser/random.js\";", source, StringComparison.Ordinal);
        Assert.Contains("export const openPage = createPageSession({", source, StringComparison.Ordinal);
        Assert.Contains("manifestUrl: new URL(\"./deployment.json\", import.meta.url).href,", source,
            StringComparison.Ordinal);
        Assert.True(source.IndexOf("\"app.analytics\": consumer0,", StringComparison.Ordinal)
            < source.IndexOf("\"app.host\": consumer1,", StringComparison.Ordinal));
        Assert.Contains("arguments: Object.freeze([\"argument\"])", source, StringComparison.Ordinal);
        Assert.Contains("Object.freeze({ name: \"MODE\", value: \"test\" })", source, StringComparison.Ordinal);
        Assert.Contains("clocks: Object.freeze([\"wall\",\"monotonic\"])", source, StringComparison.Ordinal);
        Assert.Contains("randomness: true,", source, StringComparison.Ordinal);
        Assert.Contains("compileCoreModule: WebAssembly.compile.bind(WebAssembly),", source, StringComparison.Ordinal);
        // Issue #121: only the Preview 2 families and providers the application links.
        Assert.Contains("createShim: createSelectedPreview2ShimFactory({ cli: preview2Cli, io: preview2Io, random: preview2Random }),",
            source, StringComparison.Ordinal);
        Assert.Contains("createFilesystemScope: null,", source, StringComparison.Ordinal);
        Assert.Contains("platformProviderCatalog: deepFreeze(", source, StringComparison.Ordinal);
        Assert.Contains("\"module\":\"wasi:cli/environment@0.2.11\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wasi:http/outgoing-handler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wasi:filesystem/types", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WASIShim", source, StringComparison.Ordinal);
        Assert.Contains("/hosting/deployment-manifest-validator.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("snapshotDeploymentManifest: validateDeploymentManifest,", source, StringComparison.Ordinal);
        Assert.EndsWith("});\n", source, StringComparison.Ordinal);
    }

    // Issue #121: a Release page trusts its digest-pinned manifest, so it
    // ships no manifest validator and only freezes the manifest.
    [Fact]
    public void WritesAPageHostEntryThatOnlyFreezesItsManifestWithoutValidation()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "page-entry.mjs");
        HostingCatalogFixture.Write(Path.Combine(directory.Path, "hosting"));

        new PageEntryWriter().WriteHost(HostRequest(directory.Path, output, validate: false));

        var source = File.ReadAllText(output);
        Assert.DoesNotContain("deployment-manifest-validator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("validateDeploymentManifest", source, StringComparison.Ordinal);
        Assert.Contains("snapshotDeploymentManifest: deepFreeze,", source, StringComparison.Ordinal);
        Assert.Contains("function deepFreeze(value) {", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesATypedOpenModulePinnedToOneDeployment()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "Calculator.mjs");
        var declaration = Path.Combine(directory.Path, "Calculator.d.mts");

        new PageEntryWriter().WriteModule(new(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64), output, declaration,
            [.. Exports, PageOnlyExport]));

        var source = File.ReadAllText(output);
        Assert.StartsWith("import { openPage } from \"./netwasm.page.mjs\";\n", source, StringComparison.Ordinal);
        Assert.Contains($"buildFingerprint: \"{new string('1', 64)}\",", source, StringComparison.Ordinal);
        Assert.Contains($"manifestSha256: \"{new string('2', 64)}\",", source, StringComparison.Ordinal);
        Assert.Contains("const exports = Object.freeze([\"Add\",\"Echo\",\"FetchTitle\",\"Read\",\"Reset\",\"Scale\",\"Widen\",\"Wrap\"]);", source,
            StringComparison.Ordinal);
        Assert.Contains("return openPage(Object.freeze({ exports, options, startup }));", source, StringComparison.Ordinal);
        var types = File.ReadAllText(declaration);
        Assert.Contains("  \"Add\"(argument0: number, argument1: number): number;", types, StringComparison.Ordinal);
        Assert.Contains("  \"FetchTitle\"(argument0: string | null): Promise<string | null>;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Read\"(): Promise<bigint>;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Echo\"(argument0: Uint8Array | null, argument1: string): Uint8Array | null;", types,
            StringComparison.Ordinal);
        Assert.Contains("  \"Reset\"(): void;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Scale\"(argument0: Int8Array | null, argument1: Int16Array | null, argument2: Uint16Array | null, argument3: Int32Array | null, argument4: Uint32Array | null): Float64Array | null;",
            types, StringComparison.Ordinal);
        Assert.Contains("  \"Widen\"(argument0: BigInt64Array | null, argument1: BigUint64Array | null, argument2: Float32Array | null): Promise<Float64Array | null>;",
            types, StringComparison.Ordinal);
        Assert.Contains("  \"Wrap\"(element: object | null): object | null;", types, StringComparison.Ordinal);
        Assert.Contains("  close(): Promise<void>;", types, StringComparison.Ordinal);
        Assert.Contains("  readonly failure: Promise<unknown>;", types, StringComparison.Ordinal);
        Assert.Contains("modules?: Record<string, object>;", types, StringComparison.Ordinal);
        Assert.Contains("environment?: Record<string, string>;", types, StringComparison.Ordinal);
        Assert.Contains("platform?: NetWasmPagePlatformHooks;", types, StringComparison.Ordinal);
        Assert.Contains("now?: () => number;", types, StringComparison.Ordinal);
        Assert.Contains("random?: (bytes: Uint8Array) => void;", types, StringComparison.Ordinal);
        Assert.Contains("stdout?: (line: string) => void;", types, StringComparison.Ordinal);
        Assert.Contains("export declare function open(options?: NetWasmPageOpenOptions): Promise<NetWasmPageModule>;", types,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PageAndWorkerDeclarationsPublishTheSameExports()
    {
        using var directory = TemporaryDirectory.Create();
        var page = Path.Combine(directory.Path, "App.d.mts");
        var worker = Path.Combine(directory.Path, "App.worker-client.d.mts");
        new PageEntryWriter().WriteModule(new(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64),
            Path.Combine(directory.Path, "App.mjs"), page, Exports));
        new WorkerEntryWriter().WriteClient(new(
            Path.Combine(directory.Path, "hosting"), "App.worker.mjs", new string('1', 64), new string('2', 64),
            Path.Combine(directory.Path, "App.worker-client.mjs"), worker, Exports));

        static string[] Members(string path) =>
            [.. ExportMember().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value)];
        Assert.Equal(["Add", "Echo", "FetchTitle", "Read", "Reset", "Scale", "Widen"], Members(page));
        Assert.Equal(Members(page), Members(worker));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("failure")]
    [InlineData("then")]
    public void RejectsExportNamesThePageSurfaceReserves(string name)
    {
        using var directory = TemporaryDirectory.Create();
        var request = new PageModuleRequest(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64),
            Path.Combine(directory.Path, "App.mjs"), Path.Combine(directory.Path, "App.d.mts"),
            [new(name, [], "void", null)]);

        Assert.Throws<ArgumentException>(() => new PageEntryWriter().WriteModule(request));
        Assert.False(File.Exists(request.OutputPath));
    }

    [Fact]
    public void RejectsInvalidModulePublicationContracts()
    {
        using var directory = TemporaryDirectory.Create();
        var valid = new PageModuleRequest(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64),
            Path.Combine(directory.Path, "App.mjs"), Path.Combine(directory.Path, "App.d.mts"), Exports);
        var writer = new PageEntryWriter();
        foreach (var request in new[]
                 {
                     valid with { PageFileName = "" },
                     valid with { PageFileName = "nested/netwasm.page.mjs" },
                     valid with { BuildFingerprint = "not-a-digest" },
                     valid with { ManifestSha256 = new string('A', 64) },
                     valid with { Exports = [] },
                     valid with { Exports = [new("Add", [], "void", null), new("Add", [], "void", null)] },
                     valid with { Exports = [new("Add", ["subscription"], "void", null)] },
                     valid with { Exports = [new("Add", [], "i32", "promise")] },
                     valid with { Exports = [new("Add", default, "i32", null)] },
                     valid with { OutputPath = "App.mjs" },
                 })
        {
            Assert.Throws<ArgumentException>(() => writer.WriteModule(request));
        }
        Assert.Throws<ArgumentNullException>(() => writer.WriteModule(null!));
    }

    [Fact]
    public void RejectsInvalidHostConfiguration()
    {
        using var directory = TemporaryDirectory.Create();
        var valid = HostRequest(directory.Path, Path.Combine(directory.Path, "page-entry.mjs"));
        var writer = new PageEntryWriter();
        var module = Path.Combine(directory.Path, "host.mjs");
        foreach (var request in new[]
                 {
                     valid with { Modules = default },
                     valid with { Network = "maybe" },
                     valid with { Clocks = ["sundial"] },
                     valid with { Clocks = ["wall", "wall"] },
                     valid with { Arguments = ["bad\0argument"] },
                     valid with { Environment = [new(" ", "value")] },
                     valid with { Environment = [new("MODE", "a"), new("MODE", "b")] },
                     valid with { Modules = [new("wasi:cli/environment@0.2.11", module)] },
                     valid with { Modules = [new("netwasm.host.v1", module)] },
                     valid with { Modules = [new("netwasm:platform/clock", module)] },
                     valid with { Modules = [new("app.host", module), new("app.host", module)] },
                     valid with { Modules = [new("app.host", "host.mjs")] },
                     valid with { HostingJavaScriptRoot = "hosting" },
                 })
        {
            Assert.Throws<ArgumentException>(() => writer.WriteHost(request));
        }
        Assert.Throws<ArgumentNullException>(() => writer.WriteHost(null!));
        Assert.Throws<ArgumentNullException>(() => writer.WriteHost(valid with { Modules = [null!] }));
    }

    private static PageHostEntryRequest HostRequest(string root, string output, bool validate = true) => new(
        Path.Combine(root, "hosting"),
        Path.Combine(root, "preview2-shim"),
        output,
        [new("app.host", Path.Combine(root, "host.mjs")), new("app.analytics", Path.Combine(root, "analytics.mjs"))],
        ["argument"],
        [new("MODE", "test")],
        ["wall", "monotonic"],
        "denyAll",
        true,
        ["wasi:cli/environment@0.2.11", "wasi:io/streams@0.2.11", "wasi:random/random@0.2.11", "app.host"],
        validate);

    [GeneratedRegex("^  \"([^\"]+)\"\\(", RegexOptions.Multiline)]
    private static partial Regex ExportMember();
}
