using System.Diagnostics;
using System.Security;

using NetWasm.Sdk.Pack.Restore;

using NuGet.ProjectModel;

namespace NetWasm.Sdk.Pack.Tests.Restore;

public sealed class RestoreGraphCliTests
{
    [Theory]
    [InlineData("<TargetFramework>netwasm0.1</TargetFramework>", 1)]
    [InlineData("<TargetFrameworks>netwasm0.1;net10.0</TargetFrameworks>", 2)]
    [InlineData("<TargetFramework>net10.0</TargetFramework>", 1)]
    public async Task StockGraphRoundTripPreservesCanonicalFrameworksAndProjectAliases(string frameworks, int count)
    {
        var root = Path.Combine(Path.GetTempPath(), $"netwasm-cli-roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "src/NetWasm.Sdk/Sdk/Sdk.props")))
            {
                repository = repository.Parent;
            }
            Assert.NotNull(repository);
            var sdk = SecurityElement.Escape(Path.Combine(repository.FullName, "src/NetWasm.Sdk/Sdk"));
            var versions = SecurityElement.Escape(Path.Combine(repository.FullName, "eng/NetWasm.PackageVersions.props"));
            var assembly = SecurityElement.Escape(typeof(CanonicalRestoreGraphMsBuildTask).Assembly.Location);
            var projectPath = Path.Combine(root, "Graph.csproj");
            var graphPath = Path.Combine(root, "graph.json");
            File.WriteAllText(projectPath, $"""
                <Project>
                  <Import Project="{versions}" />
                  <Import Project="{sdk}/Sdk.props" />
                  <PropertyGroup>
                    {frameworks}
                    <NetWasmSdkPackTaskAssemblyFile>{assembly}</NetWasmSdkPackTaskAssemblyFile>
                  </PropertyGroup>
                  <Import Project="{sdk}/Sdk.targets" />
                </Project>
                """);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = repository.FullName,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("msbuild");
            process.StartInfo.ArgumentList.Add(projectPath);
            process.StartInfo.ArgumentList.Add("-t:GenerateRestoreGraphFile");
            process.StartInfo.ArgumentList.Add($"-p:RestoreGraphOutputPath={graphPath}");
            process.StartInfo.ArgumentList.Add("-p:RestoreRecursive=false");
            process.StartInfo.ArgumentList.Add("-v:quiet");
            Assert.True(process.Start());
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            _ = await output;
            _ = await error;
            Assert.Equal(0, process.ExitCode);

            var graph = DependencyGraphSpec.Load(graphPath);
            var project = Assert.Single(graph.Projects);
            Assert.Equal(count, project.TargetFrameworks.Count);
            Assert.All(project.TargetFrameworks, framework => Assert.True(framework.FrameworkName.IsSpecificFramework));
            if (frameworks.Contains("netwasm0.1", StringComparison.Ordinal))
            {
                var custom = Assert.Single(project.TargetFrameworks, framework => framework.TargetAlias == "netwasm0.1");
                Assert.Equal("NetWasm,Version=v0.1", custom.FrameworkName.DotNetFrameworkName);
                Assert.Contains("netwasm0.1", project.RestoreMetadata.OriginalTargetFrameworks);
            }
            if (frameworks.Contains("net10.0", StringComparison.Ordinal))
            {
                var desktop = Assert.Single(project.TargetFrameworks, framework => framework.TargetAlias == "net10.0");
                Assert.Equal(".NETCoreApp,Version=v10.0", desktop.FrameworkName.DotNetFrameworkName);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
