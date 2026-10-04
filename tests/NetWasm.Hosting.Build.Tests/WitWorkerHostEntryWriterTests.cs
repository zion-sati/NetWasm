using System.Collections.Immutable;
using System.Text;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class WitWorkerHostEntryWriterTests
{
    [Fact]
    public void WritesPersistentComponentWorkerHost()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "generated", "worker-entry.mjs");
        var request = Valid(directory.Path) with
        {
            OutputPath = output,
            Arguments = ["argument"],
            Environment = [new("MODE", "test")],
            Clocks = ["wall"],
            ApplicationImports = [new("example:app/events", "imports/events.mjs", new string('a', 64))],
        };

        new WitWorkerHostEntryWriter().Write(request);

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var source = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", source, StringComparison.Ordinal);
        Assert.Contains("createBrowserComponentExportSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("createBrowserRawExportSession", source, StringComparison.Ordinal);
        Assert.Contains("createWorkerSessionHost", source, StringComparison.Ordinal);
        Assert.Contains("openSession: request => open({ startup: request.startup, notify: request.notify })", source,
            StringComparison.Ordinal);
        Assert.Contains("artifactPath: \"imports/events.mjs\"", source, StringComparison.Ordinal);
        Assert.Contains("module: \"example:app/events\"", source, StringComparison.Ordinal);
        Assert.Contains("clocks: Object.freeze([\"wall\"])", source, StringComparison.Ordinal);
        Assert.Contains("Object.freeze({ name: \"MODE\", value: \"test\" })", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidHostConfiguration()
    {
        using var directory = TemporaryDirectory.Create();
        var valid = Valid(directory.Path);
        WitWorkerHostEntryRequest[] invalid =
        [
            valid with { HostingJavaScriptRoot = "relative" },
            valid with { Preview2ShimRoot = string.Empty },
            valid with { OutputPath = "relative" },
            valid with { Arguments = default },
            valid with { Environment = default },
            valid with { Clocks = default },
            valid with { Network = "invalid" },
            valid with { Arguments = ["bad\0argument"] },
            valid with { Environment = [null!] },
            valid with { Environment = [new("", "value")] },
            valid with { Environment = [new("NAME\0", "value")] },
            valid with { Environment = [new("NAME", "value\0")] },
            valid with { Environment = [new("NAME", "one"), new("NAME", "two")] },
            valid with { Clocks = ["invalid"] },
            valid with { Clocks = ["wall", "wall"] },
            valid with { ApplicationImports = [new("", "events.mjs", new string('a', 64))] },
            valid with { ApplicationImports = [new("events", "../events.mjs", new string('a', 64))] },
            valid with { ApplicationImports = [new("events", "events.mjs", "invalid")] },
        ];

        Assert.Throws<ArgumentNullException>(() => new WitWorkerHostEntryWriter().Write(null!));
        foreach (var request in invalid)
        {
            Assert.Throws<ArgumentException>(() => new WitWorkerHostEntryWriter().Write(request));
        }
    }

    private static WitWorkerHostEntryRequest Valid(string root) => new(
        Path.Combine(root, "hosting"),
        Path.Combine(root, "preview2-shim"),
        Path.Combine(root, "worker-entry.mjs"),
        ImmutableArray<string>.Empty,
        ImmutableArray<WorkerEnvironmentVariable>.Empty,
        ImmutableArray<string>.Empty,
        "denyAll",
        false);
}
