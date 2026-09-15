using System.Text.Json;
using System.Xml.Linq;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class TemplateContractTests
{
    [Theory]
    [InlineData("NetWasm.App")]
    [InlineData("NetWasm.Library")]
    public void NamedProjectsPreferTheirOwnDirectory(string templateName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "NetWasm.Templates", "content", templateName,
            ".template.config", "template.json")));

        Assert.True(document.RootElement.GetProperty("preferNameDirectory").GetBoolean());
    }

    [Theory]
    [InlineData("src/NetWasm.Compiler.Tasks/NetWasm.Compiler.Tasks.csproj", "CanonicalizeNetWasmCompilerTasksPackage")]
    [InlineData("src/NetWasm.Sdk/NetWasm.Sdk.csproj", "CanonicalizeNetWasmSdkPackage")]
    [InlineData("src/NetWasm.Templates/NetWasm.Templates.csproj", "CanonicalizeNetWasmTemplatesPackage")]
    [InlineData("eng/NetWasm.DeterministicPackageArchive.targets", "CanonicalizeNetWasmPackageArchive")]
    public void PackageTargetsCanonicalizeTheirArchives(
        string projectPath,
        string targetName)
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), projectPath));

        Assert.Contains(project.Descendants("UsingTask"), task =>
            string.Equals((string?)task.Attribute("TaskName"),
                "NetWasm.Sdk.Pack.DeterministicPackageArchiveTask",
                StringComparison.Ordinal));
        var target = Assert.Single(project.Descendants("Target"), candidate =>
            string.Equals((string?)candidate.Attribute("Name"), targetName,
                StringComparison.Ordinal));
        Assert.Equal("GenerateNuspec", (string?)target.Attribute("AfterTargets"));
        Assert.Single(target.Descendants("DeterministicPackageArchiveTask"));
    }

    [Theory]
    [InlineData("src/NetWasm.Runtime.Pack/NetWasm.Runtime.Pack.csproj")]
    [InlineData("src/NetWasm.Runtime.Wasm32/NetWasm.Runtime.Wasm32.csproj")]
    [InlineData("src/NetWasm.Runtime.Wasm64/NetWasm.Runtime.Wasm64.csproj")]
    public void RuntimePackagesImportSharedArchiveCanonicalization(string projectPath)
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), projectPath));

        Assert.Contains(project.Descendants("Import"), import =>
            string.Equals(
                (string?)import.Attribute("Project"),
                "../../eng/NetWasm.DeterministicPackageArchive.targets",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NetWasm.App")]
    [InlineData("NetWasm.Library")]
    public void GeneratedProjectRequiresTheCompatibleDotNet10FeatureBand(string templateName)
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "NetWasm.Templates",
            "content",
            templateName,
            "global.json")));

        var sdk = document.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.300", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        var declaredSdkVersion = XDocument
            .Load(Path.Combine(repositoryRoot, "src", "NetWasm.Sdk", "NetWasm.Sdk.csproj"))
            .Descendants("Version")
            .Single()
            .Value;
        var generatedSdkVersion = document.RootElement
            .GetProperty("msbuild-sdks")
            .GetProperty("NetWasm.Sdk")
            .GetString();

        Assert.Equal(declaredSdkVersion, generatedSdkVersion);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "src/NetWasm.Sdk/NetWasm.Sdk.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
