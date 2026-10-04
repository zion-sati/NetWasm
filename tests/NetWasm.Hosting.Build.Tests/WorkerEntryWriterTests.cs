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
            false);

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
        Assert.Contains("globalThis.addEventListener(\"messageerror\"", source, StringComparison.Ordinal);
        Assert.Contains("host.receive(event.data).catch(fatal)", source, StringComparison.Ordinal);
        Assert.Contains("clocks: Object.freeze([\"monotonic\"])", source, StringComparison.Ordinal);
        Assert.Contains("Object.freeze({ name: \"MODE\", value: \"test\" })", source,
            StringComparison.Ordinal);
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
                new("echo", ["string", "char"], "string", null),
                new("echo_bytes", ["bytes"], "bytes", null),
            ]));

        var source = File.ReadAllText(output);
        Assert.Contains("new URL(\"./calculator.worker.mjs\", import.meta.url)", source,
            StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("worker notification handler must be a function", StringComparison.Ordinal) <
            source.IndexOf("new Worker", StringComparison.Ordinal));
        Assert.Contains("operations, onNotification,", source, StringComparison.Ordinal);
        Assert.Contains("buildFingerprint: \"1111", source, StringComparison.Ordinal);
        Assert.Contains("manifestSha256: \"2222", source, StringComparison.Ordinal);
        Assert.Contains(
            "\"add\": (argument0, argument1, options) => session.invoke(\"add\", [argument0, argument1], options)",
            source,
            StringComparison.Ordinal);
        var declaration = File.ReadAllText(declarations);
        Assert.Contains("export interface NetWasmWorkerCallOptions", declaration,
            StringComparison.Ordinal);
        Assert.Contains("signal?: AbortSignal", declaration, StringComparison.Ordinal);
        Assert.Contains("timeoutMilliseconds?: number", declaration, StringComparison.Ordinal);
        Assert.Contains(
            "\"add\"(argument0: number, argument1: number, options?: NetWasmWorkerCallOptions): Promise<number>",
            declaration, StringComparison.Ordinal);
        Assert.Contains(
            "\"read\"(options?: NetWasmWorkerCallOptions): Promise<bigint>",
            declaration, StringComparison.Ordinal);
        Assert.Contains(
            "\"echo\"(argument0: string | null, argument1: string, options?: NetWasmWorkerCallOptions): Promise<string | null>",
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

        new WorkerEntryWriter().WriteHost(new(
            Path.Combine(directory.Path, "hosting"),
            Path.Combine(directory.Path, "preview2-shim"),
            output,
            [], [], [], [], "denyAll", false));

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
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteClient(
            valid with { Exports = [new("read", [], "object", null)] }));
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteHost(new(
            valid.HostingJavaScriptRoot,
            Path.Combine(directory.Path, "shim"),
            Path.Combine(directory.Path, "host.mjs"),
            [new("wasi:cli/run@0.2.11", Path.Combine(directory.Path, "provider.mjs"), ["run"])],
            [], [], [], "denyAll", false)));
    }

    [Fact]
    public void RejectsRelativePathsAndInvalidDigests()
    {
        using var directory = TemporaryDirectory.Create();
        Assert.Throws<ArgumentException>(() => new WorkerEntryWriter().WriteHost(new(
            "hosting", Path.Combine(directory.Path, "shim"), Path.Combine(directory.Path, "host.mjs"),
            [], [], [], [], "denyAll", false)));
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
