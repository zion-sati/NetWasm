using Microsoft.Build.Utilities;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class NerdbankPackagePathRegressionTests
{
    [Theory]
    [InlineData("analyzers/dotnet/cs", "analyzers/dotnet/cs/Analyzer.dll")]
    [InlineData("analyzers/dotnet/cs/", "analyzers/dotnet/cs/Analyzer.dll")]
    [InlineData("analyzers/dotnet/cs/Analyzer.dll", "analyzers/dotnet/cs/Analyzer.dll")]
    [Trait("Issue", "52")]
    public void PackedAnalyzerDestinationPreservesTheSourceFilename(string destination, string expected)
    {
        var adapter = new[] { new MsBuildPackInputAdapter() }.Cast<IMsBuildPackInputAdapter>().Single();
        var file = RawContentItem("Analyzer.dll");
        file.SetMetadata("PackagePath", destination);
        var inputs = adapter.Adapt("Example.Analyzers", "1.0.0", "Example", "Analyzer package",
            Path.Combine(Path.GetTempPath(), "example.nupkg"), [file], [],
            CanonicalPackPlanBuilder.CanonicalTargetFramework);
        var package = PackTestFixtures.Builder().Build(inputs);
        Assert.Equal(expected, Assert.Single(package.Files).TargetPath);
    }

    [Fact]
    [Trait("Issue", "52")]
    public void RawContentDistinguishesOmittedAndEmptyPackagePath()
    {
        var omitted = RawContentItem("assets/Marker.txt");
        var empty = RawContentItem("assets/Marker.txt");
        empty.SetMetadata("PackagePath", string.Empty);

        var adapter = new MsBuildPackInputAdapter();
        var omittedFiles = adapter.Adapt("Sample", "1.0.0", "authors", "description", "sample.nupkg",
            [omitted], [], CanonicalPackPlanBuilder.CanonicalTargetFramework).Files;
        var emptyFile = Assert.Single(adapter.Adapt("Sample", "1.0.0", "authors", "description", "sample.nupkg",
            [empty], [], CanonicalPackPlanBuilder.CanonicalTargetFramework).Files);

        Assert.Equal(["content/assets/Marker.txt", "contentFiles/any/any/assets/Marker.txt"],
            omittedFiles.Select(file => file.TargetPath).Order().ToArray());
        Assert.Equal("Marker.txt", emptyFile.TargetPath);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    [Trait("Issue", "52")]
    public void RawContentRootMarkerPlacesReadmeAtPackageRoot(string destination)
    {
        var file = RawContentItem("docs/README.md");
        file.SetMetadata("PackagePath", destination);
        var inputs = new MsBuildPackInputAdapter().Adapt("Sample", "1.0.0", "authors", "description",
            Path.Combine(Path.GetTempPath(), "sample.nupkg"), [file], [],
            CanonicalPackPlanBuilder.CanonicalTargetFramework);

        var package = PackTestFixtures.Builder().Build(inputs);

        Assert.Equal("README.md", Assert.Single(package.Files).TargetPath);
    }

    private static TaskItem RawContentItem(string identity)
    {
        var item = new TaskItem(identity);
        item.SetMetadata("SourcePath", Path.GetFullPath(identity));
        item.SetMetadata("NetWasmRawContent", "true");
        item.SetMetadata("NetWasmContentTargetFolders", "content;contentFiles");
        item.SetMetadata("PackKind", nameof(PackageFileKind.Content));
        return item;
    }
}
