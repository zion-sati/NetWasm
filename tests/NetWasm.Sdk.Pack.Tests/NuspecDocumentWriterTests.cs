using System.Text;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class NuspecDocumentWriterTests
{
    [Fact]
    public void WriterEmitsStandardMetadataAndExactCanonicalGroup()
    {
        var package = PackTestFixtures.Builder().Build(PackTestFixtures.Inputs() with
        {
            Metadata = new PackageMetadata(
                "NetWasm",
                "description",
                Title: "title",
                LicenseExpression: "MIT",
                Tags: "netwasm;wasm",
                ReleaseNotes: "release",
                RepositoryUrl: "https://example.invalid/repo",
                RepositoryType: "git",
                RepositoryBranch: "refs/heads/main",
                RepositoryCommit: "0123456789abcdef0123456789abcdef01234567",
                PublishRepositoryUrl: true,
                PackageTypes: "DotnetTool;Dependency")
        });

        var xml = Encoding.UTF8.GetString(new NuspecDocumentWriter().Write(package));
        Assert.Contains("targetFramework=\"NetWasm,Version=v0.1\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("NetWasm0.1", xml, StringComparison.Ordinal);
        Assert.Contains("<title>title</title>", xml, StringComparison.Ordinal);
        Assert.Contains("<license type=\"expression\">MIT</license>", xml, StringComparison.Ordinal);
        Assert.Contains("<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>", xml, StringComparison.Ordinal);
        Assert.Contains("<repository url=\"https://example.invalid/repo\" type=\"git\" branch=\"refs/heads/main\" commit=\"0123456789abcdef0123456789abcdef01234567\"", xml, StringComparison.Ordinal);
        Assert.Contains("<packageType name=\"Dependency\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void WriterEscapesCompoundLicenseExpressionForOlderClients()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg") with
        {
            Metadata = new PackageMetadata("authors", "description", LicenseExpression: "MIT OR Apache-2.0")
        };

        var xml = Encoding.UTF8.GetString(new NuspecDocumentWriter().Write(package));

        Assert.Contains("<license type=\"expression\">MIT OR Apache-2.0</license>", xml, StringComparison.Ordinal);
        Assert.Contains("<licenseUrl>https://licenses.nuget.org/MIT%20OR%20Apache-2.0</licenseUrl>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void WriterSuppressesDependenciesWhenRequested()
    {
        var package = PackTestFixtures.Builder().Build(PackTestFixtures.Inputs() with { SuppressDependencies = true });
        var xml = Encoding.UTF8.GetString(new NuspecDocumentWriter().Write(package));
        Assert.DoesNotContain("<dependencies>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void WriterEmitsExactCanonicalGroupWhenItIsEmpty()
    {
        var package = PackTestFixtures.Builder().Build(PackTestFixtures.Inputs(dependencies: []));
        var xml = Encoding.UTF8.GetString(new NuspecDocumentWriter().Write(package));

        Assert.Contains("<dependencies>", xml, StringComparison.Ordinal);
        Assert.Contains("<group targetFramework=\"NetWasm,Version=v0.1\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<dependency id=", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void WriterRejectsUnknownOrDuplicateGroups()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg") with
        {
            Targets = [TargetProfile.NetWasmV01],
            DependencyGroups =
            [
                new CanonicalPackageDependencyGroup("NetWasm0.1", [new CanonicalPackageDependency("Dependency", "[1.0.0]")])
            ]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => new NuspecDocumentWriter().Write(package)).Code);
        var duplicate = PackTestFixtures.Package("/tmp/sample.nupkg") with
        {
            DependencyGroups =
            [
                new CanonicalPackageDependencyGroup(CanonicalPackPlanBuilder.CanonicalTargetFramework, []),
                new CanonicalPackageDependencyGroup(CanonicalPackPlanBuilder.CanonicalTargetFramework, [])
            ]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new NuspecDocumentWriter().Write(duplicate)).Code);
    }

    [Fact]
    public void WriterIsDeterministic()
    {
        var package = PackTestFixtures.Builder().Build(PackTestFixtures.Inputs());
        Assert.Equal(new NuspecDocumentWriter().Write(package), new NuspecDocumentWriter().Write(package));
    }

    [Fact]
    public void WriterEmitsFileLicenseFlagsAndAssetFilters()
    {
        var package = PackTestFixtures.Package("/tmp/sample.nupkg") with
        {
            Metadata = new PackageMetadata("authors", "description", LicenseFile: "LICENSE.txt", DevelopmentDependency: true, Serviceable: true),
            DependencyGroups = [new CanonicalPackageDependencyGroup(
                CanonicalPackPlanBuilder.CanonicalTargetFramework,
                [new CanonicalPackageDependency("Dependency", "[1.0.0]") { IncludeAssets = "compile", ExcludeAssets = "runtime" }])]
        };
        var xml = Encoding.UTF8.GetString(new NuspecDocumentWriter().Write(package));
        Assert.Contains("<license type=\"file\">LICENSE.txt</license>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<licenseUrl>", xml, StringComparison.Ordinal);
        Assert.Contains("<developmentDependency />", xml, StringComparison.Ordinal);
        Assert.Contains("<serviceable />", xml, StringComparison.Ordinal);
        Assert.Contains("include=\"compile\"", xml, StringComparison.Ordinal);
        Assert.Contains("exclude=\"runtime\"", xml, StringComparison.Ordinal);
    }
}
