// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        // Issue #128: the page imports the shared runtime its workers load too.
        Assert.Contains("import { createPageSession } from \"netwasm:runtime/page\";", source, StringComparison.Ordinal);
        Assert.Contains("import { createWasiPlatform, webPlatform } from \"netwasm:runtime/page\";", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("file://", source.Split("consumer0")[0], StringComparison.Ordinal);
        Assert.Contains("export const openPage = createPageSession({", source, StringComparison.Ordinal);
        Assert.Contains("manifestUrl: new URL(\"./deployment.json\", import.meta.url).href,", source,
            StringComparison.Ordinal);
        Assert.True(source.IndexOf("\"app.analytics\": consumer0,", StringComparison.Ordinal)
            < source.IndexOf("\"app.host\": consumer1,", StringComparison.Ordinal));
        Assert.Contains("arguments: Object.freeze([\"argument\"])", source, StringComparison.Ordinal);
        Assert.Contains("Object.freeze({ name: \"MODE\", value: \"test\" })", source, StringComparison.Ordinal);
        Assert.Contains("clocks: Object.freeze([\"wall\",\"monotonic\"])", source, StringComparison.Ordinal);
        Assert.Contains("randomness: true,", source, StringComparison.Ordinal);
        Assert.Contains("temporaryDirectory: false,", source, StringComparison.Ordinal);
        Assert.Contains("workingDirectory: false,", source, StringComparison.Ordinal);
        Assert.Contains("    ...webPlatform,\n", source, StringComparison.Ordinal);
        BrowserApiBindings.AssertNoneAtImport(source);
        // Issue #121: only the providers the application links.
        Assert.Contains("createImports: createWasiPlatform(Object.freeze({ initialDirectory: null, network: null })),",
            source, StringComparison.Ordinal);
        Assert.Contains("createFilesystemScope: null,", source, StringComparison.Ordinal);
        Assert.Contains("createSidecarMaterializer: null,", source, StringComparison.Ordinal);
        Assert.Contains("platformProviderCatalog: deepFreeze(", source, StringComparison.Ordinal);
        Assert.Contains("\"module\":\"wasi:cli/environment@0.3.2\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wasi:http/client", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wasi:filesystem/types", source, StringComparison.Ordinal);
        Assert.DoesNotContain("preview2", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("import { validateDeploymentManifest } from \"netwasm:runtime/manifest-validator\";", source,
            StringComparison.Ordinal);
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
        Assert.DoesNotContain("manifest-validator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("validateDeploymentManifest", source, StringComparison.Ordinal);
        Assert.Contains("snapshotDeploymentManifest: deepFreeze,", source, StringComparison.Ordinal);
        Assert.Contains("function deepFreeze(value) {", source, StringComparison.Ordinal);
    }

    // Issue #178: Node.js code imports a host bundled from the hosting sources, which takes
    // Node.js's web actions in place of a browser's.
    [Fact]
    public void WritesANodeHostEntryOverTheHostingSources()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "node-entry.mjs");
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting);
        string Import(string names, string file) =>
            $"import {{ {names} }} from \"{new Uri(Path.Combine(hosting, file)).AbsoluteUri}\";";

        new PageEntryWriter().WriteHost(HostRequest(directory.Path, output) with
        {
            RequiredImportModules = ["wasi:cli/environment@0.3.2", "wasi:cli/stdout@0.3.2",
                "wasi:random/random@0.3.2", "wasi:http/client@0.3.2", "wasi:filesystem/types@0.3.2",
                "app.host"],
            Platform = PageHostPlatform.Node,
        });

        var source = File.ReadAllText(output);
        Assert.DoesNotContain("netwasm:runtime/", source, StringComparison.Ordinal);
        Assert.Contains(Import("createPageSession", "page-session-root.mjs"), source, StringComparison.Ordinal);
        Assert.Contains(Import("createWasiPlatform", "wasi-platform.mjs"), source, StringComparison.Ordinal);
        Assert.Contains(Import("nodePlatform as webPlatform", "node-web-platform.mjs"), source,
            StringComparison.Ordinal);
        Assert.Contains(Import("createWasiFilesystem", "wasi-filesystem.mjs"), source, StringComparison.Ordinal);
        Assert.Contains(Import("createWasiHttpFetch", "wasi-http-fetch.mjs"), source, StringComparison.Ordinal);
        Assert.Contains(Import("validateDeploymentManifest", "deployment-manifest-validator.mjs"), source,
            StringComparison.Ordinal);
        Assert.Contains("manifestUrl: new URL(\"./deployment.json\", import.meta.url).href,", source,
            StringComparison.Ordinal);
        Assert.Contains("fetch: webPlatform.fetch", source, StringComparison.Ordinal);
        Assert.Contains("    ...webPlatform,\n", source, StringComparison.Ordinal);

        new PageEntryWriter().WriteHost(HostRequest(directory.Path, output, validate: false) with
        {
            Platform = PageHostPlatform.Node,
        });
        var plain = File.ReadAllText(output);
        Assert.DoesNotContain("createWasiFilesystem", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("createWasiHttpFetch", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("validateDeploymentManifest", plain, StringComparison.Ordinal);
    }

    // DateTime and DateTimeOffset cross as a JavaScript Date, and nullables as T | null.
    [Fact]
    public void DeclaresDatesAndNullablesAsJavaScriptValues()
    {
        using var directory = TemporaryDirectory.Create();
        var declaration = Path.Combine(directory.Path, "Dates.d.mts");

        new PageEntryWriter().WriteModule(new(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64), Path.Combine(directory.Path, "Dates.mjs"),
            declaration, [new("Shift", ["date", "i32"], "date", null), new("Later", ["date"], "date", "task"),
                new("Maybe", ["i32?", "date?"], "i64?", null),
                new("Values", ["any", "error", "string[]", "object[]"], "any[]", null)]));

        var types = File.ReadAllText(declaration);
        Assert.Contains("  \"Shift\"(argument0: Date, argument1: number): Date;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Later\"(argument0: Date, options?: NetWasmCallOptions): Promise<Date>;", types,
            StringComparison.Ordinal);
        // A nullable scalar or date is the value or null.
        Assert.Contains("  \"Maybe\"(argument0: number | null, argument1: Date | null): bigint | null;", types,
            StringComparison.Ordinal);
        // An object is any value; an exception an Error; arrays hold their elements or null.
        Assert.Contains("  \"Values\"(argument0: unknown, argument1: Error | null, argument2: (string | null)[] | null, "
            + "argument3: (object | null)[] | null): unknown[] | null;", types, StringComparison.Ordinal);
    }

    // A page that links a file system interface re-exports its file mounts (#142); others carry none.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReExportsFileMountsOnlyForAPageThatUsesFiles(bool fileMounts)
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "Files.mjs");
        var declaration = Path.Combine(directory.Path, "Files.d.mts");

        new PageEntryWriter().WriteModule(new(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64), output, declaration,
            [new("Run", [], "i32", null)])
        {
            FileMounts = fileMounts,
        });

        Assert.Equal(fileMounts, File.ReadAllText(output).Contains(
            "export { mount, sync, unmount } from \"./netwasm.page.mjs\";", StringComparison.Ordinal));
        Assert.Equal(fileMounts, File.ReadAllText(declaration).Contains(
            "export declare function mount(guestPath: string, source: NetWasmMountSource): Promise<void>;",
            StringComparison.Ordinal));
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
        // An asynchronous export takes { signal }, as a worker call does.
        Assert.Contains("export interface NetWasmCallOptions {", types, StringComparison.Ordinal);
        Assert.Contains("  signal?: AbortSignal;", types, StringComparison.Ordinal);
        Assert.Contains("  \"FetchTitle\"(argument0: string | null, options?: NetWasmCallOptions): Promise<string | null>;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Read\"(options?: NetWasmCallOptions): Promise<bigint>;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Echo\"(argument0: Uint8Array | null, argument1: string): Uint8Array | null;", types,
            StringComparison.Ordinal);
        Assert.Contains("  \"Reset\"(): void;", types, StringComparison.Ordinal);
        Assert.Contains("  \"Scale\"(argument0: Int8Array | null, argument1: Int16Array | null, argument2: Uint16Array | null, argument3: Int32Array | null, argument4: Uint32Array | null): Float64Array | null;",
            types, StringComparison.Ordinal);
        Assert.Contains("  \"Widen\"(argument0: BigInt64Array | null, argument1: BigUint64Array | null, argument2: Float32Array | null, options?: NetWasmCallOptions): Promise<Float64Array | null>;",
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

    // A browser page's module imports the bundle when it loads, so the bundle, the runtime modules and the startup
    // files download together, and open() hands the runtime the responses.
    [Fact]
    public void WritesAPageModuleThatStartsItsStartupDownloadsTogether()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "App.mjs");
        var declaration = Path.Combine(directory.Path, "App.d.mts");

        new PageEntryWriter().WriteModule(new(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64), output, declaration, Exports)
        {
            StartupLoad = new(["./netwasm.runtime.page.abc.mjs", "../runtime/netwasm.runtime.x_Y-1.mjs"],
                ["App.wasm", "App.raw-adapter.mjs", "symbols/App.wasm.type-symbols"]),
        });

        var source = File.ReadAllText(output);
        Assert.DoesNotContain("import {", source, StringComparison.Ordinal);
        Assert.Contains("const page = import(\"./netwasm.page.mjs\");\npage.catch(() => {});\n", source,
            StringComparison.Ordinal);
        Assert.Contains("for (const path of [\"./netwasm.page.mjs\",\"./netwasm.runtime.page.abc.mjs\","
            + "\"../runtime/netwasm.runtime.x_Y-1.mjs\"]) {", source, StringComparison.Ordinal);
        Assert.Contains("    link.rel = \"modulepreload\";", source, StringComparison.Ordinal);
        Assert.Contains("for (const path of [\"deployment.json\",\"App.wasm\",\"App.raw-adapter.mjs\","
            + "\"symbols/App.wasm.type-symbols\"]) {", source, StringComparison.Ordinal);
        Assert.Contains("  prefetched.set(url, response);", source, StringComparison.Ordinal);
        Assert.Contains("return page.then(({ openPage }) => openPage(Object.freeze({ exports, options, prefetched, startup })));",
            source, StringComparison.Ordinal);
        Assert.DoesNotContain("mount", source, StringComparison.Ordinal);
        Assert.Contains("export declare function open(options?: NetWasmPageOpenOptions): Promise<NetWasmPageModule>;",
            File.ReadAllText(declaration), StringComparison.Ordinal);
    }

    // A page that loads its bundle when the module loads reaches its file mounts through the loaded bundle; before it
    // has loaded, nothing can be mounted.
    [Fact]
    public void ForwardsFileMountsToThePageBundleOnceItLoads()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "Files.mjs");

        new PageEntryWriter().WriteModule(new(
            "netwasm.page.mjs", new string('1', 64), new string('2', 64), output,
            Path.Combine(directory.Path, "Files.d.mts"), [new("Run", [], "i32", null)])
        {
            FileMounts = true,
            StartupLoad = new([], []),
        });

        var source = File.ReadAllText(output);
        Assert.DoesNotContain("export { mount, sync, unmount }", source, StringComparison.Ordinal);
        Assert.Contains("for (const path of [\"deployment.json\"]) {", source, StringComparison.Ordinal);
        Assert.Contains("export async function mount(guestPath, source) {\n  return (await page).mount(guestPath, source);\n}",
            source, StringComparison.Ordinal);
        Assert.Contains("export async function sync(guestPath) {\n  return (await page).sync(guestPath);\n}", source,
            StringComparison.Ordinal);
        Assert.Contains("  if (loaded === null) throw new TypeError(`'${guestPath}' is not mounted`);\n"
            + "  return loaded.unmount(guestPath);", source, StringComparison.Ordinal);
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
            "App.worker.mjs", new string('1', 64), new string('2', 64),
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
                     valid with { StartupLoad = new(default, []) },
                     valid with { StartupLoad = new([], default) },
                     valid with { StartupLoad = new(["https://example.test/netwasm.runtime.a.mjs"], []) },
                     valid with { StartupLoad = new(["/netwasm.runtime.a.mjs"], []) },
                     valid with { StartupLoad = new(["./a/../b.mjs"], []) },
                     valid with { StartupLoad = new(["./a.mjs", "./a.mjs"], []) },
                     valid with { StartupLoad = new([], ["App.wasm", "App.wasm"]) },
                     valid with { StartupLoad = new([], ["deployment.json"]) },
                     valid with { StartupLoad = new([], ["App wasm"]) },
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
        output,
        [new("app.host", Path.Combine(root, "host.mjs")), new("app.analytics", Path.Combine(root, "analytics.mjs"))],
        ["argument"],
        [new("MODE", "test")],
        ["wall", "monotonic"],
        "denyAll",
        true,
        ["wasi:cli/environment@0.3.2", "wasi:cli/stdout@0.3.2", "wasi:random/random@0.3.2", "app.host"],
        validate);

    [GeneratedRegex("^  \"([^\"]+)\"\\(", RegexOptions.Multiline)]
    private static partial Regex ExportMember();
}
