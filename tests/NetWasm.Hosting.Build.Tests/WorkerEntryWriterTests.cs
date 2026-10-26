using System.Collections.Immutable;
using System.Text;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class WorkerEntryWriterTests
{
    [Fact]
    public void WritesStaticWorkerHostImportsAndPersistentSessionComposition()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "worker-entry.mjs");
        HostingCatalogFixture.Write(Path.Combine(directory.Path, "hosting"));
        var request = new WorkerHostEntryRequest(
            Path.Combine(directory.Path, "hosting"),
            Path.Combine(directory.Path, "preview2-shim"),
            output,
            [new("consumer.worker", Path.Combine(directory.Path, "consumer.mjs"),
                ["increment", "read"])],
            ["argument"],
            [new("MODE", "test")],
            ["monotonic"],
            "denyAll",
            false,
            ["wasi:cli/environment@0.2.11", "wasi:io/streams@0.2.11", "wasi:random/random@0.2.11", "consumer.worker"],
            true);

        new WorkerEntryWriter().WriteHost(request);

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var source = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", source, StringComparison.Ordinal);
        Assert.Contains("createBrowserRawExportSession", source, StringComparison.Ordinal);
        Assert.Contains("createWorkerSessionHost", source, StringComparison.Ordinal);
        Assert.Contains("\"consumer.worker\": Object.freeze", source, StringComparison.Ordinal);
        Assert.Contains("\"increment\": imports0[\"increment\"]", source, StringComparison.Ordinal);
        Assert.Contains("./deployment.json", source, StringComparison.Ordinal);
        Assert.Contains("globalThis.addEventListener(\"message\"", source, StringComparison.Ordinal);
        Assert.Contains("/hosting/deployment-manifest-validator.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("snapshotDeploymentManifest: validateDeploymentManifest,", source, StringComparison.Ordinal);
        Assert.Contains("globalThis.addEventListener(\"messageerror\"", source, StringComparison.Ordinal);
        Assert.Contains("host.receive(event.data).catch(fatal)", source, StringComparison.Ordinal);
        Assert.Contains("clocks: Object.freeze([\"monotonic\"])", source, StringComparison.Ordinal);
        Assert.Contains("configuration: mergeEnvironment(configuration, request.environment),", source,
            StringComparison.Ordinal);
        Assert.Contains("/session-options.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("stderr: request.stderr,", source, StringComparison.Ordinal);
        Assert.Contains("stdout: request.stdout,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("console[", source, StringComparison.Ordinal);
        Assert.Contains("Object.freeze({ name: \"MODE\", value: \"test\" })", source,
            StringComparison.Ordinal);
        // Issue #121: only the Preview 2 families and providers the application links.
        Assert.Contains("createShim: createSelectedPreview2ShimFactory({ cli: preview2Cli, io: preview2Io, random: preview2Random }),",
            source, StringComparison.Ordinal);
        Assert.Contains("createFilesystemScope: null,", source, StringComparison.Ordinal);
        Assert.Contains("\"module\":\"wasi:random/random@0.2.11\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wasi:http/outgoing-handler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WASIShim", source, StringComparison.Ordinal);
        Assert.DoesNotContain("preview2Http", source, StringComparison.Ordinal);
        Assert.True(source.LastIndexOf("import ", StringComparison.Ordinal)
            < source.IndexOf("function deepFreeze", StringComparison.Ordinal));

        // Without validation the worker only freezes its digest-pinned manifest.
        new WorkerEntryWriter().WriteHost(request with { ValidateDeploymentManifest = false });
        var trusted = File.ReadAllText(output);
        Assert.DoesNotContain("deployment-manifest-validator", trusted, StringComparison.Ordinal);
        Assert.Contains("snapshotDeploymentManifest: deepFreeze,", trusted, StringComparison.Ordinal);
    }

    // An import module binds every function its JavaScript module exports; a
    // module with no functions to bind is invalid unless it is an import module.
    [Fact]
    public void BindsEveryFunctionOfAnImportModule()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "worker-entry.mjs");
        HostingCatalogFixture.Write(Path.Combine(directory.Path, "hosting"));
        WorkerHostEntryRequest Request(bool bindsModule) => new(
            Path.Combine(directory.Path, "hosting"),
            Path.Combine(directory.Path, "preview2-shim"),
            output,
            [new("host_ui", Path.Combine(directory.Path, "host-ui.mjs"), []) { BindsModule = bindsModule }],
            [],
            [],
            [],
            "denyAll",
            false,
            [],
            true);

        new WorkerEntryWriter().WriteHost(Request(bindsModule: true));

        Assert.Contains(
            "...Object.fromEntries(Object.entries(imports0).filter(([name, value]) => name !== \"createWorkerImports\" && typeof value === \"function\")),",
            File.ReadAllText(output), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteHost(Request(bindsModule: false)));
    }

    [Fact]
    public void WritesPinnedNamedClientAndTypeScriptDeclarations()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "calculator.worker-client.mjs");
        var declarations = Path.Combine(directory.Path, "calculator.worker-client.d.mts");
        new WorkerEntryWriter().WriteClient(new(
            Path.Combine(directory.Path, "hosting"),
            "calculator.worker.mjs",
            new string('1', 64),
            new string('2', 64),
            output,
            declarations,
            [
                new("add", ["i32", "i32"], "i32", null),
                new("read", [], "i64", "task"),
                new("echo", ["string", "char"], "string", null) { ParameterNames = ["session", "title"] },
                new("greet", ["string", "string", "f64[]"], "string", null)
                {
                    ParameterNames = ["name", "title", "values"],
                    NonNullableParameters = [true, false, true],
                    NonNullableResult = true,
                },
                new("echo_bytes", ["bytes"], "bytes", null),
            ]));

        var source = File.ReadAllText(output);
        Assert.Contains("new URL(\"./calculator.worker.mjs\", import.meta.url)", source,
            StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("worker notification handler must be a function", StringComparison.Ordinal) <
            source.IndexOf("new Worker", StringComparison.Ordinal));
        Assert.Contains("operations, onNotification,", source, StringComparison.Ordinal);
        Assert.Contains("environment: options.environment, stderr: options.stderr, stdout: options.stdout,", source,
            StringComparison.Ordinal);
        Assert.Contains("buildFingerprint: \"1111", source, StringComparison.Ordinal);
        Assert.Contains("manifestSha256: \"2222", source, StringComparison.Ordinal);
        Assert.Contains(
            "\"add\": (argument0, argument1, options) => session.invoke(\"add\", [argument0, argument1], options)",
            source,
            StringComparison.Ordinal);
        // The client body calls session, so a parameter of that name falls back.
        Assert.Contains(
            "\"echo\": (argument0, title, options) => session.invoke(\"echo\", [argument0, title], options)",
            source,
            StringComparison.Ordinal);
        var declaration = File.ReadAllText(declarations);
        Assert.Contains("\"echo\"(argument0: string | null, title: string, options?: NetWasmWorkerCallOptions)",
            declaration, StringComparison.Ordinal);
        // Non-nullable C# references drop | null.
        Assert.Contains(
            "\"greet\"(name: string, title: string | null, values: Float64Array, options?: NetWasmWorkerCallOptions): Promise<string>;",
            declaration, StringComparison.Ordinal);
        Assert.Contains("export interface NetWasmWorkerCallOptions", declaration,
            StringComparison.Ordinal);
        Assert.Contains("signal?: AbortSignal", declaration, StringComparison.Ordinal);
        Assert.Contains("timeoutMilliseconds?: number", declaration, StringComparison.Ordinal);
        Assert.Contains("environment?: Record<string, string>; stdout?: (line: string) => void;", declaration,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"add\"(argument0: number, argument1: number, options?: NetWasmWorkerCallOptions): Promise<number>",
            declaration, StringComparison.Ordinal);
        Assert.Contains(
            "\"read\"(options?: NetWasmWorkerCallOptions): Promise<bigint>",
            declaration, StringComparison.Ordinal);
        Assert.Contains(
            "\"echo\"(argument0: string | null, title: string, options?: NetWasmWorkerCallOptions): Promise<string | null>",
            declaration,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"echo_bytes\"(argument0: Uint8Array | null, options?: NetWasmWorkerCallOptions): Promise<Uint8Array | null>",
            declaration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesExplicitCapabilityDenials()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "worker-entry.mjs");
        HostingCatalogFixture.Write(Path.Combine(directory.Path, "hosting"));

        new WorkerEntryWriter().WriteHost(new(
            Path.Combine(directory.Path, "hosting"),
            Path.Combine(directory.Path, "preview2-shim"),
            output,
            [], [], [], [], "denyAll", false, [], true));

        var source = File.ReadAllText(output);
        Assert.Contains("clocks: Object.freeze([])", source, StringComparison.Ordinal);
        Assert.Contains("network: \"denyAll\"", source, StringComparison.Ordinal);
        Assert.Contains("randomness: false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsReservedAndUncloneablePublicContracts()
    {
        using var directory = TemporaryDirectory.Create();
        var valid = new WorkerClientEntryRequest(
            Path.Combine(directory.Path, "hosting"),
            "app.worker.mjs",
            new string('1', 64),
            new string('2', 64),
            Path.Combine(directory.Path, "client.mjs"),
            Path.Combine(directory.Path, "client.d.mts"),
            [new("add", ["i32", "i32"], "i32", null)]);

        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteClient(
            valid with { Exports = [new("then", [], "void", null)] }));
        Assert.Throws<JavaScriptExportRealmException>(() => new WorkerEntryWriter().WriteClient(
            valid with { Exports = [new("read", [], "object", null)] }));
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteHost(new(
            valid.HostingJavaScriptRoot,
            Path.Combine(directory.Path, "shim"),
            Path.Combine(directory.Path, "host.mjs"),
            [new("wasi:cli/run@0.2.11", Path.Combine(directory.Path, "provider.mjs"), ["run"])],
            [], [], [], "denyAll", false, [], true)));
    }

    [Fact]
    public void RejectsRelativePathsAndInvalidDigests()
    {
        using var directory = TemporaryDirectory.Create();
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteHost(new(
            "hosting", Path.Combine(directory.Path, "shim"), Path.Combine(directory.Path, "host.mjs"),
            [], [], [], [], "denyAll", false, [], true)));
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteClient(new(
            Path.Combine(directory.Path, "hosting"),
            "app.worker.mjs",
            "invalid",
            new string('2', 64),
            Path.Combine(directory.Path, "client.mjs"),
            Path.Combine(directory.Path, "client.d.mts"),
            [new("add", ImmutableArray<string>.Empty, "i32", null)])));
    }
}
