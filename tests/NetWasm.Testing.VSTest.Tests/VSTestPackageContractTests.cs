using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest.Tests;

public sealed class VSTestPackageContractTests
{
    private static readonly string[] ExpectedEntries =
    [
        "LICENSE.txt",
        "NetWasm.Testing.VSTest.nuspec",
        "README.md",
        "THIRD-PARTY-NOTICES.txt",
        "TestHost-THIRD-PARTY-NOTICES.txt",
        "VSTestRunner-THIRD-PARTY-NOTICES.txt",
        "[Content_Types].xml",
        "_rels/.rels",
        "buildTransitive/NetWasm.Testing.VSTest.targets",
        "package/services/metadata/core-properties/core-properties.psmdcp",
        "tools/net10.0/any/NetWasm.Hosting.dll",
        "tools/net10.0/any/NetWasm.Testing.VSTest.RuntimeProvider.dll",
        "tools/net10.0/any/testhost/Microsoft.TestPlatform.CommunicationUtilities.dll",
        "tools/net10.0/any/testhost/Microsoft.TestPlatform.CoreUtilities.dll",
        "tools/net10.0/any/testhost/Microsoft.TestPlatform.CrossPlatEngine.dll",
        "tools/net10.0/any/testhost/Microsoft.TestPlatform.PlatformAbstractions.dll",
        "tools/net10.0/any/testhost/Microsoft.TestPlatform.Utilities.dll",
        "tools/net10.0/any/testhost/Microsoft.VisualStudio.TestPlatform.Common.dll",
        "tools/net10.0/any/testhost/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll",
        "tools/net10.0/any/testhost/packages/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll",
        "tools/net10.0/any/testhost/testhost.deps.json",
        "tools/net10.0/any/testhost/testhost.dll",
        "tools/net10.0/any/testhost/testhost.manifest.json",
        "tools/net10.0/any/testhost/testhost.runtimeconfig.json",
        "tools/net10.0/any/vstest/Microsoft.CodeCoverage.IO.dll",
        "tools/net10.0/any/vstest/Microsoft.Extensions.DependencyModel.dll",
        "tools/net10.0/any/vstest/Microsoft.Extensions.FileSystemGlobbing.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.CommunicationUtilities.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.CoreUtilities.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.CrossPlatEngine.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.PlatformAbstractions.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.Utilities.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll",
        "tools/net10.0/any/vstest/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.xml",
        "tools/net10.0/any/vstest/Microsoft.VisualStudio.TestPlatform.Client.dll",
        "tools/net10.0/any/vstest/Microsoft.VisualStudio.TestPlatform.Common.dll",
        "tools/net10.0/any/vstest/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll",
        "tools/net10.0/any/vstest/Newtonsoft.Json.dll",
        "tools/net10.0/any/vstest/datacollector.deps.json",
        "tools/net10.0/any/vstest/datacollector.dll",
        "tools/net10.0/any/vstest/datacollector.dll.config",
        "tools/net10.0/any/vstest/datacollector.runtimeconfig.json",
        "tools/net10.0/any/vstest/vstest.console.deps.json",
        "tools/net10.0/any/vstest/vstest.console.dll",
        "tools/net10.0/any/vstest/vstest.console.dll.config",
        "tools/net10.0/any/vstest/vstest.console.runtimeconfig.json",
    ];

    [Fact]
    public async Task PackContainsOnlyThePortableRuntimeProviderContract()
    {
        using var directory = TemporaryDirectory.Create();
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "NetWasm.Testing.VSTest",
            "NetWasm.Testing.VSTest.csproj");
        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetPath(),
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(directory.Path);

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
        var packagePath = Assert.Single(Directory.EnumerateFiles(directory.Path, "*.nupkg"));
        using (var package = ZipFile.OpenRead(packagePath))
        {
            Assert.Equal(
                ExpectedEntries,
                package.Entries
                    .Select(entry => entry.FullName)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

            var targets = ReadText(package, "buildTransitive/NetWasm.Testing.VSTest.targets");
            AssertBuildTransitiveContract(targets);

            var nuspec = XDocument.Parse(
                ReadText(package, "NetWasm.Testing.VSTest.nuspec"),
                LoadOptions.None);
            Assert.DoesNotContain(
                nuspec.Descendants(),
                element => element.Name.LocalName is "dependencies" or "dependency");

            var provider = package.GetEntry(
                "tools/net10.0/any/NetWasm.Testing.VSTest.RuntimeProvider.dll");
            Assert.NotNull(provider);
            using var providerBytes = new MemoryStream();
            using (var providerStream = provider.Open())
            {
                providerStream.CopyTo(providerBytes);
            }
            providerBytes.Position = 0;
            using var pe = new PEReader(providerBytes);
            var metadata = pe.GetMetadataReader();
            var references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .ToArray();
            Assert.DoesNotContain("System.Xml.XDocument", references);
            Assert.DoesNotContain("System.Xml.ReaderWriter", references);
            var threadingReference = metadata.AssemblyReferences
                .Select(metadata.GetAssemblyReference)
                .Single(reference => metadata.GetString(reference.Name) == "System.Threading");
            Assert.True(threadingReference.Version.Major <= 8);
        }

        var extractionRoot = Path.Combine(directory.Path, "extracted");
        ZipFile.ExtractToDirectory(packagePath, extractionRoot);
        var toolsRoot = Path.Combine(extractionRoot, "tools", "net10.0", "any");
        var assets = new PortableTestHostAssetResolver(toolsRoot).Resolve();
        Assert.Equal(Path.Combine(toolsRoot, "testhost", "testhost.dll"), assets.TestHostPath);
    }

    private static void AssertBuildTransitiveContract(string text)
    {
        var document = XDocument.Parse(text, LoadOptions.None);
        var propertyGroup = Assert.Single(document.Root!.Elements("PropertyGroup"));
        Assert.Equal(
            "'$(TargetFrameworkIdentifier)' == 'NetWasm' AND '$(TargetFrameworkVersion)' == 'v0.1'",
            propertyGroup.Attribute("Condition")!.Value);
        Assert.Equal(
            "RunConfiguration.TestAdapterLoadingStrategy=Explicit,DefaultRuntimeProviders,ExtensionsDirectory",
            propertyGroup.Element("_NetWasmVSTestLoadingStrategy")!.Value);
        Assert.Equal(
            "$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)../tools/net10.0/any'))",
            propertyGroup.Element("_NetWasmVSTestExtensionPath")!.Value);
        Assert.Equal(
            "$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)../tools/net10.0/any/vstest/vstest.console.dll'))",
            propertyGroup.Element("_NetWasmVSTestConsolePath")!.Value);
        Assert.Equal(
            "$([System.IO.Path]::Combine('$(MSBuildToolsPath)', 'vstest.console.dll'))",
            propertyGroup.Element("_NetWasmSdkVSTestConsolePath")!.Value);
        var consolePath = propertyGroup.Element("VSTestConsolePath");
        Assert.NotNull(consolePath);
        Assert.Equal(
            "'$(VSTestConsolePath)' == '' OR '$(VSTestConsolePath)' == '$(_NetWasmSdkVSTestConsolePath)'",
            consolePath.Attribute("Condition")!.Value);
        Assert.Equal("$(_NetWasmVSTestConsolePath)", consolePath.Value);
        AssertConditionalAppendOrder(propertyGroup, "VSTestTestAdapterPath");
        AssertConditionalAppendOrder(propertyGroup, "VSTestCLIRunSettings");
        Assert.Empty(document.Descendants("Import"));
        Assert.Empty(document.Descendants("Target"));
        Assert.DoesNotContain("Microsoft.NET.Test.Sdk", text, StringComparison.Ordinal);
    }

    private static void AssertConditionalAppendOrder(XElement propertyGroup, string propertyName)
    {
        var properties = propertyGroup.Elements(propertyName).ToArray();
        Assert.Equal(2, properties.Length);
        Assert.Equal($"'$({propertyName})' != ''", properties[0].Attribute("Condition")!.Value);
        Assert.Equal($"'$({propertyName})' == ''", properties[1].Attribute("Condition")!.Value);
    }

    private static string ReadText(ZipArchive package, string path)
    {
        var entry = package.GetEntry(path);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string DotnetPath()
    {
        var path = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(
                directory.FullName,
                "src",
                "NetWasm.Testing.VSTest",
                "NetWasm.Testing.VSTest.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        internal string Path { get; }

        internal static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-vstest-package-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
