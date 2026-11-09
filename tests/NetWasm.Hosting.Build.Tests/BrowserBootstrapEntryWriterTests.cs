using System.Text;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class BrowserBootstrapEntryWriterTests
{
    [Fact]
    public void WritesAnAppBoundBrowserEntryWithoutEmbeddingHostSources()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "browser-entry.mjs");
        var request = new BrowserBootstrapEntryRequest(
            Path.Combine(directory.Path, "hosting", "browser.mjs"),
            output);

        new BrowserBootstrapEntryWriter().Write(request);

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var source = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", source, StringComparison.Ordinal);
        Assert.Contains("createBrowserNetWasmBootstrap", source, StringComparison.Ordinal);
        Assert.Contains("export const executeNetWasm", source, StringComparison.Ordinal);
        Assert.Contains("/command-run.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("export const runNetWasm = createCommandRun(executeNetWasm);", source, StringComparison.Ordinal);
        Assert.Contains("./deployment.json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("function executeNetWasm(", source, StringComparison.Ordinal);
        Assert.Contains("/hosting/browser-web-platform.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("  ...webPlatform,\n", source, StringComparison.Ordinal);
        Assert.Contains("/hosting/wasi-platform.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("/hosting/wasi-filesystem.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains("createImports: createWasiPlatform(Object.freeze({ initialDirectory: null, network: Object.freeze({ modules: createWasiHttpFetch(Object.freeze({ fetch: webPlatform.fetch })) }) })),", source, StringComparison.Ordinal);
        Assert.Contains("  wasi,\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("preview2", source, StringComparison.OrdinalIgnoreCase);
        BrowserApiBindings.AssertNoneAtImport(source);
    }

    [Fact]
    public void RejectsRelativePathsBeforeWriting()
    {
        Assert.Throws<ArgumentException>(() => new BrowserBootstrapEntryWriter().Write(new(
            "browser.mjs",
            Path.GetFullPath("entry.mjs"))));
    }

    [Fact]
    public void ImportsAndBindsApplicationModulesInTheOrdinaryBrowserEntry()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "browser-entry.mjs");
        var request = new BrowserBootstrapEntryRequest(
            Path.Combine(directory.Path, "hosting", "browser.mjs"),
            output,
            [new("netwasm:worker/Calculator", Path.Combine(directory.Path, "Calculator.mjs"))]);

        new BrowserBootstrapEntryWriter().Write(request);

        var source = File.ReadAllText(output);
        Assert.Contains("import * as consumerModule0", source, StringComparison.Ordinal);
        Assert.Contains("Object.freeze({ [\"netwasm:worker/Calculator\"]: consumerModule0 })", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAmbiguousAndInvalidModuleBindingsBeforeWriting()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "unwritten", "browser-entry.mjs");
        var request = new BrowserBootstrapEntryRequest(
            Path.Combine(directory.Path, "hosting", "browser.mjs"),
            output);
        foreach (var modules in new System.Collections.Immutable.ImmutableArray<BrowserJavaScriptModule>[]
        {
            [new("", Path.Combine(directory.Path, "module.mjs"))],
            [new("module", "relative.mjs")],
            [new("module", Path.Combine(directory.Path, "one.mjs")), new("module", Path.Combine(directory.Path, "two.mjs"))],
        })
            Assert.Throws<ArgumentException>(() => new BrowserBootstrapEntryWriter().Write(request with { Modules = modules }));
        Assert.False(File.Exists(output));
    }
}
