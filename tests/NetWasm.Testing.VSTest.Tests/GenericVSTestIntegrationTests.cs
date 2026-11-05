using System.Diagnostics;
using System.Xml.Linq;
using NetWasm.Testing.VSTest.Tests.Adapter;

namespace NetWasm.Testing.VSTest.Tests;

public sealed class GenericVSTestIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StockVSTestPreservesFrameworkNeutralAdapterOutcome(bool failCanary)
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
        if (failCanary)
        {
            startInfo.Environment["NETWASM_VSTEST_FAIL_CANARY"] = "1";
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
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
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }

        Assert.Equal(failCanary ? 1 : 0, process.ExitCode);
        var counters = XDocument.Load(fixture.ResultPath)
            .Descendants()
            .Single(element => element.Name.LocalName == "Counters");
        Assert.Equal("1", counters.Attribute("total")!.Value);
        Assert.Equal(failCanary ? "0" : "1", counters.Attribute("passed")!.Value);
        Assert.Equal(failCanary ? "1" : "0", counters.Attribute("failed")!.Value);
    }

    private static string DotnetPath() => NetWasm.TestInfrastructure.DotNetHost.Executable;
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
        // Stock VSTest from the .NET 10 SDK, as a consumer's global.json selects it.
        // VSTest 18.11 in the .NET 11 RC3 SDK does not load runtime providers from
        // /TestAdapterPath, so it cannot run this source.
        File.WriteAllText(
            Path.Combine(fixture.Root, "global.json"),
            $$"""{ "sdk": { "version": "{{Sdk10MinimumVersion()}}", "rollForward": "latestFeature" } }""");

        var providerRoot = Path.GetDirectoryName(typeof(NetWasmTestRuntimeProvider).Assembly.Location)!;
        File.Copy(typeof(NetWasmTestRuntimeProvider).Assembly.Location, fixture.ProviderPath);
        File.Copy(typeof(SyntheticNetWasmTestDiscoverer).Assembly.Location, fixture.AdapterPath);
        CopyDirectory(
            Path.Combine(providerRoot, "testhost"),
            Path.Combine(fixture.ExtensionRoot, "testhost"));
        return fixture;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static string Sdk10MinimumVersion()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "eng", "csharp15-toolchain.json")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException(
                "The repository's eng/csharp15-toolchain.json was not found.");
        }
        using var toolchain = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(directory.FullName, "eng", "csharp15-toolchain.json")));
        return toolchain.RootElement.GetProperty("sdk10MinimumVersion").GetString()!;
    }

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
