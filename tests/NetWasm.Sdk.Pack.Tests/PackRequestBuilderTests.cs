using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class PackRequestBuilderTests
{
    private readonly CanonicalPackPlanBuilder builder = PackTestFixtures.Builder();

    [Fact]
    public void BuildPreservesCanonicalProfileAndSortsFilesAndDependencies()
    {
        var package = builder.Build(PackTestFixtures.Inputs(
            files:
            [
                new CanonicalPackageFileInput("b.dll", "lib/NetWasm,Version=v0.1/B.dll"),
                new CanonicalPackageFileInput("a.dll", "lib/NetWasm,Version=v0.1/A.dll")
            ],
            dependencies:
            [
                new CanonicalPackageDependencyInput("Z.Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework),
                new CanonicalPackageDependencyInput("A.Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
            ]));

        Assert.Equal("0.1.0", package.Version);
        Assert.Equal("lib/NetWasm,Version=v0.1/A.dll", package.Files[0].TargetPath);
        Assert.Equal("A.Dependency", package.DependencyGroups[0].Dependencies[0].Id);
        Assert.Equal(TargetProfile.NetWasmV01, package.Targets[0]);
    }

    [Fact]
    public void BuildResolvesExplicitRegisteredProfilesAndRejectsDuplicateOrUnknownGroups()
    {
        var explicitTargets = PackTestFixtures.Inputs(dependencies: []) with { Targets = [TargetProfile.NetWasmV01] };
        Assert.Single(builder.Build(explicitTargets).Targets);

        var mismatchedRegisteredTarget = explicitTargets with
        {
            Targets = [TargetProfile.NetWasmV01 with { CanonicalFolder = "Other,Version=v1", CanonicalDependencyGroup = "Other,Version=v1" }]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(mismatchedRegisteredTarget)).Code);

        var duplicateTargets = explicitTargets with { Targets = [TargetProfile.NetWasmV01, TargetProfile.NetWasmV01] };
        Assert.Equal(NetWasmPackErrorCode.NWPK016, Assert.Throws<NetWasmPackException>(() => builder.Build(duplicateTargets)).Code);

        var unknownDependency = PackTestFixtures.Inputs(dependencies: [new CanonicalPackageDependencyInput("Unknown", "[1.0.0]", "desktop")]);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => builder.Build(unknownDependency)).Code);
    }

    [Fact]
    public void BuildIncludesOptInSourceAndMetadata()
    {
        var inputs = PackTestFixtures.Inputs() with
        {
            Metadata = new PackageMetadata("NetWasm", "sample package", Title: "Sample", Tags: "netwasm"),
            Source = new SourceInputs(true, [new SourceInput("source.cs", "src/source.cs")], false)
        };
        var package = builder.Build(inputs);
        Assert.Equal("Sample", package.Metadata.Title);
        Assert.Contains(package.Files, file => file.TargetPath == "src/source.cs");
    }

    [Fact]
    public void BuildPreservesRegisteredDependencyGroupWhenItHasNoDependencies()
    {
        var package = builder.Build(PackTestFixtures.Inputs(dependencies: []));

        var group = Assert.Single(package.DependencyGroups);
        Assert.Equal(CanonicalPackPlanBuilder.CanonicalTargetFramework, group.TargetFramework);
        Assert.Equal(TargetProfile.NetWasmV01.Alias, group.TargetFrameworkAlias);
        Assert.Empty(group.Dependencies);
    }

    [Fact]
    public void BuildRejectsDeclaredMetadataFilesAbsentFromPackageInputs()
    {
        var readme = PackTestFixtures.Inputs() with
        {
            Metadata = new PackageMetadata("NetWasm", "sample package", Readme: "README.md")
        };
        var license = readme with
        {
            Metadata = new PackageMetadata("NetWasm", "sample package", LicenseFile: "LICENSE.txt")
        };
        var icon = readme with
        {
            Metadata = new PackageMetadata("NetWasm", "sample package", Icon: "icon.svg")
        };

        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(readme)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(license)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(icon)).Code);
    }

    [Fact]
    public void BuildAcceptsDeclaredMetadataFilesAndValidTargetAlias()
    {
        var inputs = PackTestFixtures.Inputs(
            files:
            [
                new CanonicalPackageFileInput("sample.dll", "lib/NetWasm,Version=v0.1/Sample.dll")
                {
                    TargetFrameworkAlias = "netwasm0.1"
                },
                new CanonicalPackageFileInput("README.md", "README.md"),
                new CanonicalPackageFileInput("LICENSE.txt", "LICENSE.txt"),
                new CanonicalPackageFileInput("icon.svg", "icon.svg")
            ],
            dependencies: []) with
        {
            Metadata = new PackageMetadata("NetWasm", "sample package", LicenseFile: "LICENSE.txt", Icon: "icon.svg", Readme: "README.md")
        };
        var package = builder.Build(inputs);

        Assert.Contains(package.Files, file => file.TargetPath == "README.md");
        Assert.Equal("netwasm0.1", package.Files.Single(file => file.TargetPath.Contains("Sample.dll", StringComparison.Ordinal)).TargetFrameworkAlias);
    }

    [Fact]
    public void BuildSortsMultipleSourceInputs()
    {
        var inputs = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true,
            [
                new SourceInput("z.cs", "src/z.cs"),
                new SourceInput("a.cs", "src/a.cs")
            ], false)
        };
        var files = builder.Build(inputs).Files.Where(file => file.SourcePath is "a.cs" or "z.cs").ToArray();
        Assert.Equal("src/a.cs", files[0].TargetPath);
        Assert.Equal("src/z.cs", files[1].TargetPath);
    }

    [Theory]
    [InlineData("netwasm0.1")]
    [InlineData("NetWasm0.1")]
    [InlineData("netstandard2.0")]
    public void BuildRejectsNonCanonicalCustomIdentity(string target)
    {
        var error = Assert.Throws<NetWasmPackException>(() => builder.Build(PackTestFixtures.Inputs(target: target)));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, error.Code);
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:\\absolute.dll")]
    [InlineData("lib/./escape.dll")]
    [InlineData("lib//escape.dll")]
    public void BuildRejectsUnsafePackagePath(string path)
    {
        var error = Assert.Throws<NetWasmPackException>(() => builder.Build(PackTestFixtures.Inputs(files: [new CanonicalPackageFileInput("a.dll", path)])));
        Assert.Equal(NetWasmPackErrorCode.NWPK009, error.Code);
    }

    [Fact]
    public void BuildRejectsNormalizedFrameworkPathsAndMismatchedTargetAliases()
    {
        var normalized = PackTestFixtures.Inputs(files: [new CanonicalPackageFileInput("a.dll", "lib/netwasm0.1/a.dll")]);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(normalized)).Code);
        var mismatchedAlias = PackTestFixtures.Inputs(files: [new CanonicalPackageFileInput("a.dll", "lib/other/a.dll") { TargetFrameworkAlias = "netwasm0.1" }]);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(mismatchedAlias)).Code);
        var unknownAlias = PackTestFixtures.Inputs(files: [new CanonicalPackageFileInput("a.dll", "lib/other/a.dll") { TargetFrameworkAlias = "desktop" }]);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(unknownAlias)).Code);
    }

    [Fact]
    public void BuildRejectsDuplicateAndReservedPackagePaths()
    {
        var duplicate = PackTestFixtures.Inputs(files:
        [
            new CanonicalPackageFileInput("a.dll", "lib/a.dll"),
            new CanonicalPackageFileInput("b.dll", "lib/A.dll")
        ]);
        var reserved = PackTestFixtures.Inputs(files: [new CanonicalPackageFileInput("a", "NetWasm.Sample.nuspec")]);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => builder.Build(duplicate)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => builder.Build(reserved)).Code);
    }

    [Fact]
    public void BuildRejectsDuplicateDependencyAndPrivateEdge()
    {
        var duplicate = PackTestFixtures.Inputs(dependencies:
        [
            new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework),
            new CanonicalPackageDependencyInput("dependency", "[2.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
        ]);
        var privateDependency = PackTestFixtures.Inputs(dependencies:
        [new CanonicalPackageDependencyInput("Private", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework) { PrivateAssets = "all" }]);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => builder.Build(duplicate)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK007, Assert.Throws<NetWasmPackException>(() => builder.Build(privateDependency)).Code);
    }

    [Fact]
    public void BuildRejectsNullCollectionsAndIncompleteRestorePolicy()
    {
        var nullFiles = PackTestFixtures.Inputs() with { Files = null! };
        var nullDependencies = PackTestFixtures.Inputs() with { Dependencies = null! };
        var stale = PackTestFixtures.Inputs() with { Restore = new RestoreEvidence("assets.json", string.Empty, null, null, [], "", "") };
        Assert.Throws<ArgumentNullException>(() => builder.Build(nullFiles));
        Assert.Throws<ArgumentNullException>(() => builder.Build(nullDependencies));
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => builder.Build(stale)).Code);
    }

    [Fact]
    public void BuildHonorsExplicitDependencySuppressionAndSymbolPolicy()
    {
        var suppressed = PackTestFixtures.Inputs() with { SuppressDependencies = true };
        var package = builder.Build(suppressed);
        Assert.Empty(package.DependencyGroups);
        var unsupported = PackTestFixtures.Inputs() with { Symbols = new SymbolInputs(true, "symbols.nupkg", []) };
        Assert.Equal(NetWasmPackErrorCode.NWPK012, Assert.Throws<NetWasmPackException>(() => builder.Build(unsupported)).Code);
    }

    [Fact]
    public void BuildRejectsInvalidOptionsAndMissingInputs()
    {
        var invalidTimestamp = PackTestFixtures.Inputs(dependencies: []) with { Determinism = DeterminismPolicy.Default with { EntryTimestamp = new DateTimeOffset(1979, 1, 1, 0, 0, 0, TimeSpan.Zero) } };
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => builder.Build(invalidTimestamp)).Code);
        var invalidUpperTimestamp = PackTestFixtures.Inputs(dependencies: []) with { Determinism = DeterminismPolicy.Default with { EntryTimestamp = new DateTimeOffset(2108, 1, 1, 0, 0, 0, TimeSpan.Zero) } };
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => builder.Build(invalidUpperTimestamp)).Code);
        var invalidCompression = PackTestFixtures.Inputs(dependencies: []) with { Determinism = DeterminismPolicy.Default with { CompressionLevel = 10 } };
        Assert.Equal(NetWasmPackErrorCode.NWPK013, Assert.Throws<NetWasmPackException>(() => builder.Build(invalidCompression)).Code);
        var missingSymbols = PackTestFixtures.Inputs(dependencies: []) with { Symbols = null! };
        Assert.Equal(NetWasmPackErrorCode.NWPK012, Assert.Throws<NetWasmPackException>(() => builder.Build(missingSymbols)).Code);
        var nullSymbol = PackTestFixtures.Inputs(dependencies: []) with { Symbols = new SymbolInputs(true, "snupkg", [null!]) };
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(nullSymbol)).Code);
        var controlSource = PackTestFixtures.Inputs(dependencies: []) with { Files = [new CanonicalPackageFileInput("a\u0000.dll", "lib/a.dll")] };
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => builder.Build(controlSource)).Code);
        var missingSource = PackTestFixtures.Inputs(dependencies: []) with { Files = [new CanonicalPackageFileInput("", "lib/a.dll")] };
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(missingSource)).Code);
        var invalidOutput = PackTestFixtures.Inputs(dependencies: []) with { OutputPath = "\u0000" };
        Assert.Equal(NetWasmPackErrorCode.NWPK014, Assert.Throws<NetWasmPackException>(() => builder.Build(invalidOutput)).Code);
    }

    [Fact]
    public void BuildPreservesNetWasmAndDesktopTargetOutputIdentities()
    {
        var identities = new TargetOutputIdentity[]
        {
            TargetOutputIdentity.NetWasmV01,
            new("net10.0", ".NETCoreApp", "v10.0", "net10.0", null)
        };
        var inputs = PackTestFixtures.Inputs(files:
        [
            new CanonicalPackageFileInput("netwasm.dll", "lib/NetWasm,Version=v0.1/Sample.dll"),
            new CanonicalPackageFileInput("desktop.dll", "lib/net10.0/Sample.dll")
        ]) with
        {
            TargetIdentities = identities
        };

        var package = builder.Build(inputs);
        Assert.Equal(identities, package.TargetIdentities);
    }

    [Fact]
    public void BuildMapsDesktopRestoreTargetAndRejectsUnknownRestoreKeys()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "assets.json");
        File.WriteAllText(assets, "{\"targets\":{\"net10.0\":{}}}");
        var identities = new TargetOutputIdentity[] { TargetOutputIdentity.NetWasmV01, new("net10.0", ".NETCoreApp", "v10.0", "net10.0", null) };
        var input = PackTestFixtures.Inputs(dependencies: []) with
        {
            TargetIdentities = identities,
            Restore = new RestoreEvidence(assets, Hash(assets), null, null, ["net10.0"], "", "") { Required = true }
        };
        var package = builder.Build(input);
        Assert.Equal("net10.0", package.Restore.TargetKeys.Single());
        File.WriteAllText(assets, "{\"targets\":{\"unknown\":{}}}");
        var unknown = input with { Restore = input.Restore with { AssetsFileHash = Hash(assets), TargetKeys = ["unknown"] } };
        Assert.Equal(NetWasmPackErrorCode.NWPK005, Assert.Throws<NetWasmPackException>(() => builder.Build(unknown)).Code);
    }

    [Fact]
    public void BuildRejectsRestoreParityGapsAndSourceRootEscapes()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "assets.json");
        File.WriteAllText(assets, "{\"targets\":{\"NetWasm,Version=v0.1\":{\"Producer/1.0.0\":{\"type\":\"project\",\"dependencies\":{\"Other\":\"1.0.0\"}}}}}");
        var baseInput = PackTestFixtures.Inputs(dependencies: [new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)]) with
        {
            Restore = new RestoreEvidence(assets, Hash(assets), null, null, [CanonicalPackPlanBuilder.CanonicalTargetFramework], "", "") { Required = true }
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => builder.Build(baseInput)).Code);
        File.WriteAllText(assets, "{\"targets\":{\"NetWasm,Version=v0.1\":{}}}");
        var noGraph = baseInput with { Restore = baseInput.Restore with { AssetsFileHash = Hash(assets) } };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => builder.Build(noGraph)).Code);

        var outside = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true, [new SourceInput(Path.Combine(directory.Path, "outside.cs"), "src/outside.cs", directory.Path)], false)
        };
        File.WriteAllText(Path.Combine(directory.Path, "outside.cs"), "source");
        Assert.Throws<NetWasmPackException>(() => builder.Build(outside with { Source = outside.Source with { Files = [new SourceInput("/tmp/outside.cs", "src/outside.cs", directory.Path)] } }));

        var invalidRoot = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true, [new SourceInput("source.cs", "src/source.cs", "\u0000")], false)
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => builder.Build(invalidRoot)).Code);

        var root = Path.Combine(directory.Path, "root");
        var external = Path.Combine(directory.Path, "external.cs");
        var link = Path.Combine(root, "linked.cs");
        Directory.CreateDirectory(root);
        File.WriteAllText(external, "source");
        File.CreateSymbolicLink(link, external);
        var linkInput = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true, [new SourceInput(link, "src/linked.cs", root)], false)
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => builder.Build(linkInput)).Code);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    [Fact]
    public void BuildRejectsMalformedOrDuplicateTargetOutputIdentities()
    {
        var duplicate = PackTestFixtures.Inputs() with
        {
            TargetIdentities = [TargetOutputIdentity.NetWasmV01, TargetOutputIdentity.NetWasmV01]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK016, Assert.Throws<NetWasmPackException>(() => builder.Build(duplicate)).Code);

        var malformed = PackTestFixtures.Inputs() with
        {
            TargetIdentities = [TargetOutputIdentity.NetWasmV01 with { AssetFolder = "NetWasm0.1" }]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(malformed)).Code);

        var desktopGroup = PackTestFixtures.Inputs() with
        {
            TargetIdentities = [new TargetOutputIdentity("net10.0", ".NETCoreApp", "v10.0", "net10.0", "net10.0")]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK001, Assert.Throws<NetWasmPackException>(() => builder.Build(desktopGroup)).Code);
    }

    [Fact]
    public void BuildPreservesDesktopProfileAndRejectsCustomIdentityMismatchesThroughInjectedActors()
    {
        var desktopProfile = new TargetProfile("desktop", "Desktop", "v1", "Desktop,Version=v1", "Desktop,Version=v1");
        var withDesktop = CreateBuilder(new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]));
        var withBoth = PackTestFixtures.Inputs(dependencies: []) with { Targets = [TargetProfile.NetWasmV01, desktopProfile] };
        Assert.Equal(2, withDesktop.Build(withBoth).Targets.Length);
        var withoutCustom = PackTestFixtures.Inputs(dependencies: []) with { Targets = [desktopProfile] };
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => withDesktop.Build(withoutCustom)).Code);

        var mismatchedIdentity = CreateBuilder(targetIdentityValidator: new FixedTargetIdentityValidator([TargetOutputIdentity.NetWasmV01 with { Identifier = "Other" }]));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => mismatchedIdentity.Build(PackTestFixtures.Inputs(dependencies: []) with { TargetIdentities = [TargetOutputIdentity.NetWasmV01] })).Code);

        var mismatchedGroup = CreateBuilder(targetIdentityValidator: new FixedTargetIdentityValidator([TargetOutputIdentity.NetWasmV01 with { DependencyGroup = "Other" }]));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => mismatchedGroup.Build(PackTestFixtures.Inputs(dependencies: []) with { TargetIdentities = [TargetOutputIdentity.NetWasmV01] })).Code);
        var noCustom = CreateBuilder(targetIdentityValidator: new FixedTargetIdentityValidator([]));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => noCustom.Build(PackTestFixtures.Inputs(dependencies: []))).Code);
        var identities = new[] { TargetOutputIdentity.NetWasmV01, new TargetOutputIdentity("desktop", "Desktop", "v1", "desktop", null) };
        var targetInput = PackTestFixtures.Inputs(dependencies: [new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)]) with
        {
            Targets = [TargetProfile.NetWasmV01, desktopProfile],
            TargetIdentities = identities
        };
        var wrongFramework = new RestoreEvidence("assets", "hash", null, null, ["desktop"], "", "")
        {
            Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", "desktop", false, false) { VersionRange = "[1.0.0]" }]
        };
        var wrongFrameworkBuilder = CreateBuilder(profileResolver: new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]), restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(wrongFramework));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => wrongFrameworkBuilder.Build(targetInput with { Restore = wrongFramework })).Code);
        var wrongVersion = wrongFramework with
        {
            Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false) { VersionRange = "[2.0.0]" }]
        };
        var wrongVersionBuilder = CreateBuilder(profileResolver: new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]), restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(wrongVersion));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => wrongVersionBuilder.Build(targetInput with { Restore = wrongVersion })).Code);
        var matchingEvidence = wrongVersion with
        {
            TargetKeys = [CanonicalPackPlanBuilder.CanonicalTargetFramework],
            Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false) { VersionRange = "[1.0.0]" }]
        };
        var matchingBuilder = CreateBuilder(profileResolver: new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]), restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(matchingEvidence));
        Assert.NotNull(matchingBuilder.Build(targetInput with { Restore = matchingEvidence }));
        var fallbackInput = targetInput with
        {
            Dependencies = [new CanonicalPackageDependencyInput("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework)]
        };
        var fallbackEvidence = matchingEvidence with
        {
            Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false)]
        };
        var fallbackBuilder = CreateBuilder(profileResolver: new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]), restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(fallbackEvidence));
        Assert.NotNull(fallbackBuilder.Build(fallbackInput with { Restore = fallbackEvidence }));

        var minimumInput = targetInput with
        {
            Dependencies = [new CanonicalPackageDependencyInput("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework)]
        };
        var minimumEvidence = matchingEvidence with
        {
            Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false) { VersionRange = "[1.0.0, )" }]
        };
        var minimumBuilder = CreateBuilder(profileResolver: new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]), restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(minimumEvidence));
        Assert.NotNull(minimumBuilder.Build(minimumInput with { Restore = minimumEvidence }));

        var malformedEvidence = matchingEvidence with
        {
            Graph = [new RestoreDependencyEvidence("Dependency", "1.0.0", CanonicalPackPlanBuilder.CanonicalTargetFramework, false, false) { VersionRange = "not-a-range" }]
        };
        var malformedBuilder = CreateBuilder(profileResolver: new ProfileRegistry([TargetProfile.NetWasmV01, desktopProfile]), restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(malformedEvidence));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => malformedBuilder.Build(fallbackInput with { Restore = malformedEvidence })).Code);
    }

    [Fact]
    public void BuildChecksRestoreHashPolicyBeforeReturningPackage()
    {
        var assets = new RestoreEvidence("assets.json", "", null, null, [CanonicalPackPlanBuilder.CanonicalTargetFramework], "", "") { Required = true };
        var configured = CreateBuilder(restoreValidator: new NoOpRestoreValidator(), restoreReader: new FixedRestoreReader(assets));
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => configured.Build(PackTestFixtures.Inputs(dependencies: []) with { Restore = assets })).Code);
    }

    [Fact]
    public void PlanBuilderRequiresEveryInjectedActor()
    {
        var validator = new RestoreEvidenceValidator();
        var reader = new FileRestoreEvidenceReader(validator);
        var cache = new PackCacheValidator(new PackRequestFingerprintBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]>())));
        var profiles = new ProfileRegistry();
        var dependencies = new DependencyPolicy();
        var paths = new PackagePathValidator();
        var identities = new PackageIdentityValidator();
        var metadata = new MetadataPolicy();
        var targets = new TargetIdentityPolicy();
        var link = new LocalLinkTargetResolver();
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(null!, dependencies, paths, identities, metadata, validator, reader, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, null!, paths, identities, metadata, validator, reader, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, null!, identities, metadata, validator, reader, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, null!, metadata, validator, reader, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, identities, null!, validator, reader, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, identities, metadata, null!, reader, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, identities, metadata, validator, null!, cache, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, identities, metadata, validator, reader, null!, targets, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, identities, metadata, validator, reader, cache, null!, link));
        Assert.Throws<ArgumentNullException>(() => new CanonicalPackPlanBuilder(profiles, dependencies, paths, identities, metadata, validator, reader, cache, targets, null!));
    }

    [Fact]
    public void BuildMapsLinkInspectionFailuresToStableSourceRootError()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source.cs");
        File.WriteAllText(source, "source");
        var input = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true, [new SourceInput(source, "src/source.cs", directory.Path)], false)
        };
        var error = Assert.Throws<NetWasmPackException>(() => CreateBuilder(linkTargetResolver: new ThrowingLinkTargetResolver()).Build(input));
        Assert.Equal(NetWasmPackErrorCode.NWPK009, error.Code);
        Assert.Null(new LocalLinkTargetResolver().Resolve(source));
        var missing = Path.Combine(directory.Path, "missing.cs");
        var missingInput = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true, [new SourceInput(missing, "src/missing.cs", directory.Path)], false)
        };
        Assert.Contains(CreateBuilder().Build(missingInput).Files, file => file.SourcePath == missing);
        var suppressed = PackTestFixtures.Inputs(dependencies: []) with
        {
            TargetIdentities = [TargetOutputIdentity.NetWasmV01 with { SuppressDependencies = true }]
        };
        Assert.Empty(CreateBuilder().Build(suppressed).DependencyGroups);
        var nullIdentities = PackTestFixtures.Inputs(dependencies: []) with { TargetIdentities = null! };
        Assert.NotNull(CreateBuilder().Build(nullIdentities));
        var nullTargets = PackTestFixtures.Inputs(dependencies: []) with { Targets = null! };
        Assert.NotNull(CreateBuilder().Build(nullTargets));
        var inside = Path.Combine(directory.Path, "inside.cs");
        File.WriteAllText(inside, "source");
        var insideInput = PackTestFixtures.Inputs(dependencies: []) with
        {
            Source = new SourceInputs(true, [new SourceInput(inside, "src/inside.cs", directory.Path)], false)
        };
        Assert.Contains(CreateBuilder().Build(insideInput).Files, file => file.SourcePath == inside);
    }

    private static CanonicalPackPlanBuilder CreateBuilder(
        IProfileResolver? profileResolver = null,
        IRestoreEvidenceValidator? restoreValidator = null,
        IRestoreEvidenceReader? restoreReader = null,
        ITargetIdentityValidator? targetIdentityValidator = null,
        ILinkTargetResolver? linkTargetResolver = null)
    {
        var validator = restoreValidator ?? new RestoreEvidenceValidator();
        return new CanonicalPackPlanBuilder(
            profileResolver ?? new ProfileRegistry(),
            new DependencyPolicy(),
            new PackagePathValidator(),
            new PackageIdentityValidator(),
            new MetadataPolicy(),
            validator,
            restoreReader ?? new FileRestoreEvidenceReader(validator),
            new PackCacheValidator(new PackRequestFingerprintBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["sample.dll"] = [1] }))),
            targetIdentityValidator ?? new TargetIdentityPolicy(),
            linkTargetResolver ?? new LocalLinkTargetResolver());
    }

    private sealed class FixedTargetIdentityValidator(ImmutableArray<TargetOutputIdentity> result) : ITargetIdentityValidator
    {
        public ImmutableArray<TargetOutputIdentity> Validate(IReadOnlyList<TargetOutputIdentity> identities) => result;
    }

    private sealed class NoOpRestoreValidator : IRestoreEvidenceValidator
    {
        public void Validate(RestoreEvidence evidence) { }
    }

    private sealed class FixedRestoreReader(RestoreEvidence result) : IRestoreEvidenceReader
    {
        public RestoreEvidence Read(RestoreEvidence evidence) => result;
    }

    private sealed class ThrowingLinkTargetResolver : ILinkTargetResolver
    {
        public string? Resolve(string path) => throw new IOException("link inspection");
    }

    [Fact]
    public void TargetIdentityPolicyRejectsNullIncompleteAndUnsafeIdentities()
    {
        var policy = new TargetIdentityPolicy();
        Assert.Equal(NetWasmPackErrorCode.NWPK001, Assert.Throws<NetWasmPackException>(() =>
            policy.Validate(new TargetOutputIdentity[] { null! })).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK001, Assert.Throws<NetWasmPackException>(() =>
            policy.Validate([TargetOutputIdentity.NetWasmV01 with { Identifier = "" }])).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() =>
            policy.Validate([TargetOutputIdentity.NetWasmV01 with { AssetFolder = "../lib" }])).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() =>
            policy.Validate([TargetOutputIdentity.NetWasmV01 with { Identifier = "Other" }])).Code);
    }

    [Fact]
    public void TargetIdentityPolicyRejectsMissingCustomTargetAndDesktopGroup()
    {
        var policy = new TargetIdentityPolicy();
        Assert.Equal(NetWasmPackErrorCode.NWPK001, Assert.Throws<NetWasmPackException>(() =>
            policy.Validate([new TargetOutputIdentity("net10.0", ".NETCoreApp", "v10.0", "net10.0", null)])).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() =>
            policy.Validate([TargetOutputIdentity.NetWasmV01, new TargetOutputIdentity("net10.0", ".NETCoreApp", "v10.0", "net10.0", "net10.0")])).Code);
    }
}
