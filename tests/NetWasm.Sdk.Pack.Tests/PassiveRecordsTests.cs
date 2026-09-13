using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class PassiveRecordsTests
{
    [Fact]
    public void CanonicalInputsAndPackageRecordsPreserveTheirImmutableValues()
    {
        var inputFile = new CanonicalPackageFileInput("source.dll", "lib/NetWasm,Version=v0.1/source.dll")
        {
            Kind = PackageFileKind.BuildOutput,
            TargetFrameworkAlias = "netwasm0.1",
            SourceRoot = "/source",
            ExpectedSha256 = "hash",
            ExpectedLength = 3
        };
        var dependencyInput = new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
        {
            TargetFrameworkAlias = "netwasm0.1",
            IncludeAssets = "runtime",
            ExcludeAssets = "compile",
            PrivateAssets = "none",
            Origin = PackageDependencyOrigin.ProjectReference
        };
        var inputs = new CanonicalPackInputs("Sample", "1.0.0", "authors", "description", "sample.nupkg", [inputFile], [dependencyInput], CanonicalPackPlanBuilder.CanonicalTargetFramework);
        Assert.Equal("Sample", inputs.Id);
        Assert.Equal("authors", inputs.Metadata.Authors);

        var file = new CanonicalPackageFile("source.dll", inputFile.TargetPath)
        {
            Kind = PackageFileKind.BuildOutput,
            TargetFrameworkAlias = "netwasm0.1",
            SourceRoot = "/source",
            ExpectedSha256 = "hash",
            ExpectedLength = 3
        };
        var dependency = new CanonicalPackageDependency("Dependency", "[1.0.0]")
        {
            IncludeAssets = "runtime",
            ExcludeAssets = "compile",
            PrivateAssets = "none"
        };
        var group = new CanonicalPackageDependencyGroup(CanonicalPackPlanBuilder.CanonicalTargetFramework, [dependency])
        {
            TargetFrameworkAlias = "netwasm0.1"
        };
        var package = new CanonicalPackage("Sample", "1.0.0", "authors", "description", "sample.nupkg", [file], [group]);
        Assert.Equal("Sample", package.Id);
        Assert.Equal("1.0.0", package.Version);
        Assert.Equal("authors", package.Authors);
        Assert.Equal("description", package.Description);
        Assert.Equal("sample.nupkg", package.OutputPath);
        Assert.Single(package.Files);
        Assert.Single(package.DependencyGroups);
    }

    [Fact]
    public void ArchiveAndRestoreRecordsPreserveOutputsAndEvidence()
    {
        var entry = ArchiveEntry.FromBytes("lib/file.dll", [1, 2], "source.dll");
        var plan = new ArchivePlan("sample.nupkg", [entry], DeterminismPolicy.Default, new PackageIdentity("Sample", "1.0.0"));
        var output = new PackageOutput("sample.nupkg", "package-hash", plan.Identity);
        var symbols = new SymbolPackageOutput("sample.snupkg", "symbol-hash", plan.Identity);
        var manifest = new CanonicalPackManifest("1", "0.1", "request", ["entry=hash"], output.Sha256, symbols.Sha256)
        {
            InputHashes = ["input=hash"],
            TargetKeys = [CanonicalPackPlanBuilder.CanonicalTargetFramework],
            PackagePath = output.Path,
            SymbolPackagePath = symbols.Path
        };
        var result = new CanonicalPackResult(output, symbols, manifest, "sample.nuspec", "manifest.json");
        var artifacts = new PackArtifacts("sample.nuspec", [1], "manifest.json", [2]);
        Assert.Equal("lib/file.dll", plan.Entries[0].Path);
        Assert.Equal("package-hash", result.Package.Sha256);
        Assert.Equal("sample.nuspec", result.NuspecPath);
        Assert.Equal("manifest.json", result.ManifestPath);
        Assert.Equal("manifest.json", artifacts.ManifestPath);

        var symbol = new SymbolInput("sample.pdb", "symbols/sample.pdb");
        var source = new SourceInput("source.cs", "src/source.cs", "/source");
        var symbolInputs = new SymbolInputs(true, "snupkg", [symbol]);
        var sourceInputs = new SourceInputs(true, [source], true, "{}");
        Assert.True(symbolInputs.IncludeSymbols);
        Assert.True(sourceInputs.IncludeSource);

        var edge = new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false)
        {
            VersionRange = "[1.0.0]",
            ResolvedVersion = "1.0.0",
            Source = "Producer/1.0.0"
        };
        var evidence = new RestoreEvidence("assets.json", "assets-hash", "packages.lock.json", "lock-hash", [CanonicalPackPlanBuilder.CanonicalTargetFramework], "6.0.0", "10.0.0")
        {
            Required = true,
            Graph = [edge]
        };
        Assert.True(evidence.Required);
        Assert.Equal("Producer/1.0.0", evidence.Graph[0].Source);
        Assert.Equal("snupkg", SymbolInputs.None.Format);
        Assert.False(SourceInputs.None.IncludeSource);
        Assert.Empty(RestoreEvidence.Unspecified.TargetKeys);
    }

    [Fact]
    public void ProfilesMetadataPoliciesAndRequestsRemainConstructible()
    {
        var profile = TargetProfile.NetWasmV01;
        var outputIdentity = TargetOutputIdentity.NetWasmV01;
        var metadata = new PackageMetadata("authors", "description", Title: "title", PublishRepositoryUrl: true, RepositoryUrl: "https://example.test");
        var request = new ProjectEvaluationRequest("net10.0", "Release", "AnyCPU", "browser-wasm");
        var projectIdentity = new ProjectPackageIdentity("Project", "1.0.0");
        var identity = new PackageIdentity("Sample", "1.0.0") { PackageType = "Dependency" };
        Assert.Equal("NetWasm,Version=v0.1", profile.CanonicalFolder);
        Assert.Equal(profile.CanonicalFolder, outputIdentity.AssetFolder);
        Assert.Equal("title", metadata.Title);
        Assert.Equal("Release", request.Configuration);
        Assert.True(projectIdentity.IsPackable);
        Assert.Equal("Dependency", identity.PackageType);
    }
}
