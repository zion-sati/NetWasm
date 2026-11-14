using System.Diagnostics;
using System.Security;
using System.Text.Json;

namespace NetWasm.Sdk.Tests;

public sealed class NerdbankReportRegressionTests
{
    [Theory]
    [InlineData("netwasm0.1")]
    [InlineData("net10.0")]
    [Trait("Issue", "51")]
    public async Task PackCollectorResolvesCentralPackageVersions(string framework)
    {
        using var result = await EvaluateAsync(framework,
            "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>",
            "<PackageVersion Include=\"Example.Package\" Version=\"1.2.3\" /><PackageReference Include=\"Example.Package\" />",
            "_NetWasmSdkInnerPackageDependency", "NetWasmSdkCollectInnerPackageReferences");
        var dependency = result.RootElement.GetProperty("Items").GetProperty("_NetWasmSdkInnerPackageDependency")
            .EnumerateArray().Single(item => item.GetProperty("Identity").GetString() == "Example.Package");
        Assert.Equal("1.2.3", dependency.GetProperty("Version").GetString());
    }

    // NetWasm.App provides System.Linq and System.Net.Http, so they are ordinary implicit usings; the user's
    // removal holds, and a project compiling against CoreLib alone has neither.
    [Theory]
    [InlineData("enable", "System.Linq", false, false, true)]
    [InlineData("true", "System.Net.Http", false, false, true)]
    [InlineData("disable", "System.Linq", false, false, false)]
    [InlineData("enable", "System.Linq", true, false, false)]
    [InlineData("enable", "System.Net.Http", true, false, false)]
    [InlineData("enable", "System.Linq", false, true, false)]
    [InlineData("enable", "System.Net.Http", false, true, false)]
    [Trait("Issue", "56")]
    public async Task LinqAndHttpAreImplicitUsingsWhenNetWasmAppProvidesThem(
        string setting, string namespaceName, bool remove, bool coreLibOnly, bool expected)
    {
        var properties = $"<ImplicitUsings>{setting}</ImplicitUsings>" +
            (coreLibOnly ? "<DisableImplicitNetWasmAppReference>true</DisableImplicitNetWasmAppReference>" : "");
        using var result = await EvaluateAsync("netwasm0.1", properties,
            remove ? $"<Using Remove=\"{namespaceName}\" />" : "", "Using");
        var usings = result.RootElement.GetProperty("Items").GetProperty("Using").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString()).ToArray();
        Assert.Equal(expected, usings.Contains(namespaceName));
        Assert.Equal(usings.Length, usings.Distinct().Count());
    }

    [Theory]
    [InlineData("netwasm0.1", false)]
    [InlineData("netwasm0.1", true)]
    [InlineData("net10.0", false)]
    [Trait("Issue", "54")]
    public async Task RestoredAnalyzerReferenceSelectsItsHostFramework(string framework, bool useOverrides)
    {
        var overrides = useOverrides
            ? " SetTargetFramework=\"TargetFramework=netstandard2.0\" SkipGetTargetFrameworkProperties=\"true\""
            : "";
        // The restore reads the published packages; NetWasm.App's are not published yet, and this concerns
        // the analyzer reference, not the framework.
        using var result = await EvaluateAsync(framework, "<DisableImplicitNetWasmAppReference>true</DisableImplicitNetWasmAppReference>",
            "<ProjectReference Include=\"analyzer/Analyzer.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\"" + overrides + " />",
            "_MSBuildProjectReferenceExistent", "PrepareProjectReferences", restoreAnalyzer: true);
        Assert.Single(result.RootElement.GetProperty("Items").GetProperty("_MSBuildProjectReferenceExistent").EnumerateArray());
    }

    private static async Task<JsonDocument> EvaluateAsync(
        string framework, string properties, string items, string itemName, string? target = null,
        bool restoreAnalyzer = false, string afterImports = "")
    {
        var repository = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repository, "src/NetWasm.Sdk/Sdk/Sdk.props")))
            repository = Directory.GetParent(repository)?.FullName ?? throw new DirectoryNotFoundException();
        var root = Path.Combine(Path.GetTempPath(), "netwasm-report-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (restoreAnalyzer)
            {
                var analyzerRoot = Path.Combine(root, "analyzer");
                Directory.CreateDirectory(analyzerRoot);
                await File.WriteAllTextAsync(Path.Combine(analyzerRoot, "Analyzer.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>");
            }
            var sdk = SecurityElement.Escape(Path.Combine(repository, "src/NetWasm.Sdk/Sdk"));
            var project = Path.Combine(root, "Probe.csproj");
            await File.WriteAllTextAsync(project, $$"""
                <Project>
                  <Import Project="{{SecurityElement.Escape(Path.Combine(repository, "eng/NetWasm.PackageVersions.props"))}}" />
                  <Import Project="{{sdk}}/Sdk.props" />
                  <PropertyGroup><TargetFramework>{{framework}}</TargetFramework>{{properties}}</PropertyGroup>
                  <ItemGroup>{{items}}</ItemGroup>
                  <Import Project="{{sdk}}/Sdk.targets" />
                  {{afterImports}}
                </Project>
                """);
            if (restoreAnalyzer)
            {
                using var restore = new Process
                {
                    StartInfo = new ProcessStartInfo("dotnet")
                    {
                        WorkingDirectory = repository,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    }
                };
                foreach (var argument in new[] { "restore", project, "--nologo" })
                    restore.StartInfo.ArgumentList.Add(argument);
                Assert.True(restore.Start());
                var restoreOutput = restore.StandardOutput.ReadToEndAsync();
                var restoreError = restore.StandardError.ReadToEndAsync();
                await restore.WaitForExitAsync();
                var restoreText = await restoreOutput + await restoreError;
                Assert.True(restore.ExitCode == 0, restoreText);
            }
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = repository,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            };
            foreach (var argument in new[] { "msbuild", project, "-nologo", "-getItem:" + itemName })
                process.StartInfo.ArgumentList.Add(argument);
            if (target is not null) process.StartInfo.ArgumentList.Add("-target:" + target);
            Assert.True(process.Start());
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var errorText = await error;
            var outputText = await output;
            Assert.True(process.ExitCode == 0, outputText + errorText);
            return JsonDocument.Parse(outputText);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
