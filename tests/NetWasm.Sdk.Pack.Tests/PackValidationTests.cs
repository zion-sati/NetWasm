using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class PackValidationTests
{
    [Fact]
    public void IdentityValidatorNormalizesNuGetSemVer()
    {
        var identity = new PackageIdentityValidator().Validate(new PackageIdentity("Sample.Id", "01.0.0+build"));
        Assert.Equal("1.0.0", identity.Version);
    }

    [Theory]
    [InlineData("bad id", "1.0.0")]
    [InlineData("bad/id", "1.0.0")]
    [InlineData("Sample", "not-a-version")]
    [InlineData("Sample", "1.0.0 ")]
    public void IdentityValidatorRejectsMalformedIdentity(string id, string version)
    {
        var error = Assert.Throws<NetWasmPackException>(() => new PackageIdentityValidator().Validate(new PackageIdentity(id, version)));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, error.Code);
    }

    [Fact]
    public void IdentityValidatorExercisesEmptyAndControlValueBranches()
    {
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new PackageIdentityValidator().Validate(new PackageIdentity("", "1.0.0"))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new PackageIdentityValidator().Validate(new PackageIdentity("Sample", "1.0.0", ""))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new PackageIdentityValidator().Validate(new PackageIdentity("Sample", "1/0.0"))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new PackageIdentityValidator().Validate(new PackageIdentity("Sample", "1.0.0\u0001"))).Code);
    }

    [Fact]
    public void IdentityValidatorRejectsMissingIdentity()
    {
        Assert.Throws<ArgumentNullException>(() => new PackageIdentityValidator().Validate(null!));
    }

    [Fact]
    public void IdentityValidatorRejectsInvalidPackageType()
    {
        var error = Assert.Throws<NetWasmPackException>(() =>
            new PackageIdentityValidator().Validate(new PackageIdentity("Sample", "1.0.0", "bad type")));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, error.Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new PackageIdentityValidator().Validate(new PackageIdentity("Sample", "1.0.0", "bad/type"))).Code);
    }

    [Fact]
    public void ProfileRegistryResolvesCaseInsensitiveAlias()
    {
        var registry = new ProfileRegistry();
        var profile = registry.Resolve("NETWASM0.1");
        Assert.Equal("NetWasm,Version=v0.1", profile.CanonicalFolder);
        Assert.Equal(profile.CanonicalFolder, profile.CanonicalDependencyGroup);
        Assert.Equal(NetWasmPackErrorCode.NWPK001, Assert.Throws<NetWasmPackException>(() => registry.Resolve(null!)).Code);
    }

    [Fact]
    public void ProfileRegistryRejectsMissingAndDuplicateProfiles()
    {
        var missing = Assert.Throws<NetWasmPackException>(() => new ProfileRegistry().Resolve("netwasm0.2"));
        Assert.Equal(NetWasmPackErrorCode.NWPK001, missing.Code);
        var duplicate = Assert.Throws<NetWasmPackException>(() => new ProfileRegistry([TargetProfile.NetWasmV01, TargetProfile.NetWasmV01]));
        Assert.Equal(NetWasmPackErrorCode.NWPK016, duplicate.Code);
        var malformed = Assert.Throws<NetWasmPackException>(() => new ProfileRegistry([TargetProfile.NetWasmV01 with { CanonicalFolder = "NetWasm0.1" }]));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, malformed.Code);
    }

    [Fact]
    public void ProfileRegistryRejectsNullIncompleteAndDuplicateCanonicalGroups()
    {
        var missing = Assert.Throws<NetWasmPackException>(() =>
            new ProfileRegistry(new[] { (TargetProfile)null! }));
        Assert.Equal(NetWasmPackErrorCode.NWPK001, missing.Code);

        var incomplete = Assert.Throws<NetWasmPackException>(() => new ProfileRegistry([
            TargetProfile.NetWasmV01 with { Version = "" }
        ]));
        Assert.Equal(NetWasmPackErrorCode.NWPK001, incomplete.Code);

        var duplicateGroup = Assert.Throws<NetWasmPackException>(() => new ProfileRegistry([
            TargetProfile.NetWasmV01,
            new TargetProfile("desktop", "Desktop", "v1", TargetProfile.NetWasmV01.CanonicalFolder, TargetProfile.NetWasmV01.CanonicalFolder)
        ]));
        Assert.Equal(NetWasmPackErrorCode.NWPK016, duplicateGroup.Code);
        var nonCanonical = Assert.Throws<NetWasmPackException>(() => new ProfileRegistry([
            new TargetProfile("desktop", "Desktop", "v1", "Desktop,Version=v2", "Desktop,Version=v2")
        ]));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, nonCanonical.Code);
    }

    [Fact]
    public void DependencyPolicyValidatesNuGetRangeAndPrivateEdges()
    {
        var policy = new DependencyPolicy();
        var dependency = policy.Validate(new CanonicalPackageDependencyInput("Dependency", "[1.0.0,2.0.0)", CanonicalPackPlanBuilder.CanonicalTargetFramework), TargetProfile.NetWasmV01);
        Assert.Equal("Dependency", dependency.Id);
        Assert.True(VersionRange.Parse(dependency.VersionRange).Satisfies(NuGetVersion.Parse("1.5.0")));
        Assert.Equal("all", dependency.IncludeAssets);
        Assert.Throws<ArgumentNullException>(() => policy.Validate(null!, TargetProfile.NetWasmV01));
        Assert.Throws<ArgumentNullException>(() => policy.Validate(new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework), null!));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => policy.Validate(
            new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
            {
                TargetFrameworkAlias = "desktop"
            }, TargetProfile.NetWasmV01)).Code);
    }

    [Theory]
    [InlineData("Dependency", "not a range", "NetWasm,Version=v0.1", "none", false, NetWasmPackErrorCode.NWPK006)]
    [InlineData("Dependency", "[1.0.0]", "net10.0", "none", false, NetWasmPackErrorCode.NWPK006)]
    [InlineData("Dependency", "[1.0.0]", "NetWasm,Version=v0.1", "compile", false, NetWasmPackErrorCode.NWPK007)]
    [InlineData("Dependency", "[1.0.0]", "NetWasm,Version=v0.1", "none", true, NetWasmPackErrorCode.NWPK007)]
    public void DependencyPolicyRejectsUnrepresentableEdges(string id, string range, string framework, string privateAssets, bool development, NetWasmPackErrorCode expected)
    {
        var input = new CanonicalPackageDependencyInput(id, range, framework) { PrivateAssets = privateAssets, IsDevelopmentDependency = development };
        var error = Assert.Throws<NetWasmPackException>(() => new DependencyPolicy().Validate(input, TargetProfile.NetWasmV01));
        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public void DependencyPolicyRejectsInvalidIdAndAssetFilter()
    {
        var policy = new DependencyPolicy();
        var invalidId = Assert.Throws<NetWasmPackException>(() => policy.Validate(
            new CanonicalPackageDependencyInput("bad id", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework),
            TargetProfile.NetWasmV01));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, invalidId.Code);

        var invalidFilter = Assert.Throws<NetWasmPackException>(() => policy.Validate(
            new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
            {
                IncludeAssets = "compile assets",
            }, TargetProfile.NetWasmV01));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, invalidFilter.Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => policy.Validate(
            new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
            {
                ExcludeAssets = "compile assets"
            }, TargetProfile.NetWasmV01)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => policy.Validate(
            new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)
            {
                IncludeAssets = "compile\u0001"
            }, TargetProfile.NetWasmV01)).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => policy.Validate(
            new CanonicalPackageDependencyInput("", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework), TargetProfile.NetWasmV01)).Code);
    }

    [Theory]
    [InlineData("lib/a.dll", "lib/a.dll")]
    [InlineData("lib\\a.dll", "lib/a.dll")]
    public void PathValidatorNormalizesSafePath(string path, string expected)
    {
        Assert.Equal(expected, new PackagePathValidator().Validate(path));
    }

    [Theory]
    [InlineData("../a.dll")]
    [InlineData("/a.dll")]
    [InlineData("C:\\a.dll")]
    [InlineData("lib//a.dll")]
    [InlineData("lib/./a.dll")]
    [InlineData("lib/\u0001.dll")]
    public void PathValidatorRejectsUnsafePath(string path)
    {
        var error = Assert.Throws<NetWasmPackException>(() => new PackagePathValidator().Validate(path));
        Assert.Equal(NetWasmPackErrorCode.NWPK009, error.Code);
    }

    [Fact]
    public void PathValidatorRejectsEmptyPath()
    {
        var error = Assert.Throws<NetWasmPackException>(() => new PackagePathValidator().Validate(" "));
        Assert.Equal(NetWasmPackErrorCode.NWPK009, error.Code);
    }

    [Fact]
    public void TargetIdentityPolicyAllowsAnEmptyOptionalIdentityList()
    {
        Assert.Empty(new TargetIdentityPolicy().Validate([]));
    }

    [Fact]
    public void MetadataPolicyRejectsInvalidXmlAndPrivateRepositoryOptIn()
    {
        var policy = new MetadataPolicy();
        Assert.Throws<NetWasmPackException>(() => policy.Validate(new PackageMetadata("A\u0001", "description")));
        var error = Assert.Throws<NetWasmPackException>(() => policy.Validate(new PackageMetadata("A", "description", PublishRepositoryUrl: true)));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, error.Code);
    }

    [Fact]
    public void MetadataPolicyRequiresAuthorsAndDescription()
    {
        var policy = new MetadataPolicy();
        var missingAuthors = Assert.Throws<NetWasmPackException>(() => policy.Validate(new PackageMetadata("", "description")));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, missingAuthors.Code);
        var missingDescription = Assert.Throws<NetWasmPackException>(() => policy.Validate(new PackageMetadata("authors", "")));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, missingDescription.Code);
    }

    [Fact]
    public void PackExceptionExposesStableSafeMessage()
    {
        var exception = new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "safe message");
        Assert.Equal("safe message", exception.SafeMessage);
        Assert.Contains("NWPK001", exception.Message, StringComparison.Ordinal);
    }
}
