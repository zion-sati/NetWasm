using System.Diagnostics;
using System.Xml.Linq;
using NetWasm.Testing.VSTest.Tests.Adapter;

namespace NetWasm.Testing.VSTest.Tests;

public sealed class GenericVSTestIntegrationTests
{
    [Fact]
    public async Task StockVSTestSelectsTheNetWasmProviderAndRunsAFrameworkNeutralAdapter()
    {
        using var fixture = GenericVSTestFixture.Create();
        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetPath(),
            WorkingDirectory = fixture.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(fixture.SourcePath);
        startInfo.ArgumentList.Add("/Framework:NetWasm,Version=v0.1");
        startInfo.ArgumentList.Add(
            $"/TestAdapterPath:{fixture.ExtensionRoot};{fixture.AdapterPath}");
        startInfo.ArgumentList.Add(
            "/TestAdapterLoadingStrategy:Explicit,DefaultRuntimeProviders,ExtensionsDirectory");
        startInfo.ArgumentList.Add($"/ResultsDirectory:{fixture.ResultsRoot}");
        startInfo.ArgumentList.Add("/Logger:trx;LogFileName=result.trx");
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(cancellation.Token);
            await Task.WhenAll(output, error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        Assert.Equal(0, process.ExitCode);
        var counters = XDocument.Load(fixture.ResultPath)
            .Descendants()
            .Single(element => element.Name.LocalName == "Counters");
        Assert.Equal("1", counters.Attribute("total")!.Value);
        Assert.Equal("1", counters.Attribute("passed")!.Value);
        Assert.Equal("0", counters.Attribute("failed")!.Value);
    }

    private static string DotnetPath()
    {
        var path = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        return path;
    }
}

internal sealed class GenericVSTestFixture : IDisposable
{
    private GenericVSTestFixture(string root)
    {
        Root = root;
        ExtensionRoot = Path.Combine(root, "extensions");
        ResultsRoot = Path.Combine(root, "results");
        SourcePath = Path.Combine(root, "Example.Tests.dll");
        ResultPath = Path.Combine(ResultsRoot, "result.trx");
        ProviderPath = Path.Combine(
            ExtensionRoot,
            Path.GetFileName(typeof(NetWasmTestRuntimeProvider).Assembly.Location));
        AdapterPath = Path.Combine(
            ExtensionRoot,
            Path.GetFileName(typeof(SyntheticNetWasmTestDiscoverer).Assembly.Location));
    }

    internal string Root { get; }

    internal string ExtensionRoot { get; }

    internal string ResultsRoot { get; }

    internal string SourcePath { get; }

    internal string ResultPath { get; }

    internal string ProviderPath { get; }

    internal string AdapterPath { get; }

    internal static GenericVSTestFixture Create()
    {
        var fixture = new GenericVSTestFixture(Path.Combine(
            Path.GetTempPath(),
            $"netwasm-generic-vstest-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(fixture.ExtensionRoot);
        Directory.CreateDirectory(fixture.ResultsRoot);
        File.WriteAllText(fixture.SourcePath, string.Empty);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "global.json"),
            Path.Combine(fixture.Root, "global.json"));

        var providerRoot = Path.GetDirectoryName(typeof(NetWasmTestRuntimeProvider).Assembly.Location)!;
        File.Copy(typeof(NetWasmTestRuntimeProvider).Assembly.Location, fixture.ProviderPath);
        File.Copy(typeof(SyntheticNetWasmTestDiscoverer).Assembly.Location, fixture.AdapterPath);
        CopyDirectory(
            Path.Combine(providerRoot, "testhost"),
            Path.Combine(fixture.ExtensionRoot, "testhost"));
        return fixture;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
