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
            Path.Combine(directory.Path, "preview2-shim"),
            output);

        new BrowserBootstrapEntryWriter().Write(request);

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var source = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", source, StringComparison.Ordinal);
        Assert.Contains("createBrowserNetWasmBootstrap", source, StringComparison.Ordinal);
        Assert.Contains("export const executeNetWasm", source, StringComparison.Ordinal);
        Assert.Contains("./deployment.json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("function executeNetWasm(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRelativePathsBeforeWriting()
    {
        Assert.Throws<ArgumentException>(() => new BrowserBootstrapEntryWriter().Write(new(
            "browser.mjs",
            Path.GetFullPath("shim"),
            Path.GetFullPath("entry.mjs"))));
    }
}
