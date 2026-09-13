using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class DeploymentManifestValidatorTests
{
    private readonly IDeploymentManifestValidator _subject = Assert.IsAssignableFrom<IDeploymentManifestValidator>(new DeploymentManifestValidator());

    [Theory]
    [InlineData(DeploymentKind.Component, "wasm32")]
    [InlineData(DeploymentKind.Raw, "wasm64")]
    [InlineData(DeploymentKind.Browser, "wasm32")]
    public void AcceptsEachSupportedKindAndTarget(DeploymentKind kind, string target) =>
        _subject.Validate(ManifestFixture.Create() with { DeploymentKind = kind, Target = target });

    [Fact]
    public void AcceptsExplicitlyEmptyFunctionInventories() =>
        _subject.Validate(ManifestFixture.Create() with
        {
            RequiredImportModules = [],
            RequiredImports = [],
            Exports = [],
        });

    [Fact]
    public void AcceptsTypeOnlyRequiredImportModule() =>
        _subject.Validate(ManifestFixture.Create() with
        {
            RequiredImportModules = [
                "wasi:cli/environment@0.2.11",
                "wasi:io/error@0.2.11",
            ],
        });

    [Theory]
    [InlineData("[constructor]descriptor")]
    [InlineData("[method]descriptor.read-via-stream")]
    [InlineData("[static]descriptor.open-at")]
    [InlineData("[resource-drop]descriptor")]
    [InlineData("[resource-dtor]descriptor")]
    [InlineData("[export-resource-new]descriptor")]
    [InlineData("[export-resource-rep]descriptor")]
    [InlineData("[export-resource-drop]descriptor")]
    public void AcceptsCanonicalWitResourceFunctionIdentities(string name)
    {
        var manifest = ManifestFixture.Create();

        _subject.Validate(manifest with
        {
            RequiredImports = [manifest.RequiredImports[0] with { Name = name }],
        });
    }

    [Fact]
    public void AcceptsReachableLocalTimeWithOrWithoutItsExactAdjacentSidecar()
    {
        var manifest = ManifestFixture.Create() with { RuntimeFeatures = ["local-time"] };
        _subject.Validate(manifest);
        _subject.Validate(manifest with
        {
            Artifacts = [.. manifest.Artifacts, TimeZoneArtifact(manifest)],
        });
    }

    [Fact]
    public void RejectsNullManifest() => Assert.Throws<ArgumentNullException>(() => _subject.Validate(null!));

    [Theory]
    [MemberData(nameof(InvalidManifests))]
    public void RejectsUnsafeIncompleteOrAmbiguousManifest(DeploymentManifest manifest) =>
        Assert.ThrowsAny<ArgumentException>(() => _subject.Validate(manifest));

    public static TheoryData<DeploymentManifest> InvalidManifests()
    {
        var valid = ManifestFixture.Create();
        var application = valid.Artifacts[0];
        var receipt = valid.Artifacts[1];
        var timeZone = TimeZoneArtifact(valid);
        var import = valid.RequiredImports[0];
        var export = valid.Exports[0];
        var data = new TheoryData<DeploymentManifest>
        {
            valid with { SchemaVersion = 0 },
            valid with { SchemaVersion = 2 },
            valid with { DeploymentKind = (DeploymentKind)99 },
            valid with { Profile = null! },
            valid with { Profile = "net10.0" },
            valid with { Target = null! },
            valid with { Target = "wasm128" },
            valid with { FeatureSet = null! },
            valid with { FeatureSet = " " },
            valid with { FeatureSet = " padded" },
            valid with { FeatureSet = "bad\nvalue" },
            valid with { FeatureSet = "bad\0value" },
            valid with { Versions = null! },
            valid with { RuntimeFeatures = default },
            valid with { RuntimeFeatures = ["local-time", "local-time"] },
            valid with { RuntimeFeatures = ["future"] },
            valid with { RuntimeFeatures = ["Local-Time"] },
            valid with { RuntimeFeatures = ["bad_feature"] },
            valid with { Artifacts = default },
            valid with { Artifacts = [] },
            valid with { Artifacts = [null!] },
            valid with { Artifacts = [receipt] },
            valid with { Artifacts = [application, application with { RelativePath = "other.wasm" }] },
            valid with { Artifacts = [application, receipt with { RelativePath = application.RelativePath }] },
            valid with { Artifacts = [application with { Role = "Application" }, receipt] },
            valid with { Artifacts = [application with { MediaType = "wasm" }, receipt] },
            valid with { Artifacts = [application with { SchemaVersion = 0 }, receipt] },
            valid with { Artifacts = [application with { SchemaVersion = -1 }, receipt] },
            valid with { Artifacts = [application, receipt, timeZone] },
            valid with
            {
                RuntimeFeatures = ["local-time"],
                Artifacts = [application, receipt, timeZone, timeZone with { RelativePath = "other.wasm.tz-info" }],
            },
            valid with
            {
                RuntimeFeatures = ["local-time"],
                Artifacts = [application, receipt, timeZone with { RelativePath = "other.wasm.tz-info" }],
            },
            valid with
            {
                RuntimeFeatures = ["local-time"],
                Artifacts = [application, receipt, timeZone with { MediaType = "application/vnd.netwasm.timezone" }],
            },
            valid with
            {
                RuntimeFeatures = ["local-time"],
                Artifacts = [application, receipt, timeZone with { SchemaVersion = null }],
            },
            valid with
            {
                RuntimeFeatures = ["local-time"],
                Artifacts = [application, receipt, timeZone with { SchemaVersion = 2 }],
            },
            valid with { RequiredImportModules = default },
            valid with { RequiredImportModules = [] },
            valid with
            {
                RequiredImportModules = [
                    valid.RequiredImportModules[0],
                    valid.RequiredImportModules[0],
                ],
            },
            valid with { RequiredImports = default },
            valid with { Exports = default },
            valid with { RequiredImports = [null!] },
            valid with { RequiredImports = [import, import] },
            valid with { RequiredImports = [import with { Parameters = default }] },
            valid with { RequiredImports = [import with { Results = default }] },
            valid with { RequiredImports = [import with { Parameters = [" "] }] },
            valid with { RequiredImports = [import with { Results = ["bad\0result"] }] },
            valid with { Exports = [export, export] },
        };

        foreach (var digest in new[] { null, "", new string('a', 63), new string('a', 65), new string('g', 64), new string('A', 64) })
        {
            data.Add(valid with { SemanticBuildId = digest! });
            data.Add(valid with { BuildFingerprint = digest! });
            data.Add(valid with { Artifacts = [application with { Sha256 = digest! }, receipt] });
        }

        foreach (var version in new[] { null, "", " ", "bad\0version" })
        {
            data.Add(valid with { Versions = valid.Versions with { Sdk = version! } });
            data.Add(valid with { Versions = valid.Versions with { Compiler = version! } });
            data.Add(valid with { Versions = valid.Versions with { Runtime = version! } });
            data.Add(valid with { Versions = valid.Versions with { RuntimeAbi = version! } });
            data.Add(valid with { Versions = valid.Versions with { Hosting = version! } });
            data.Add(valid with { Versions = valid.Versions with { Toolchain = version! } });
        }

        foreach (var path in new[] { null, "", " ", "/rooted.wasm", "C:/rooted.wasm", "nested\\file.wasm", "a//b", "./app.wasm", "a/../b", "bad\0path" })
        {
            data.Add(valid with { Artifacts = [application with { RelativePath = path! }, receipt] });
        }

        foreach (var role in new[] { null, "", " ", "Application", "bad_role", "bad\0role" })
        {
            data.Add(valid with { Artifacts = [application, receipt with { Role = role! }] });
        }

        foreach (var mediaType in new[] { null, "", " ", "json", "bad\0type/json" })
        {
            data.Add(valid with { Artifacts = [application, receipt with { MediaType = mediaType! }] });
        }

        foreach (var identifier in new[] { null, "", " ", "@1.0.0", "wasi-command@", "a@b@1.0.0", "Wasi@1.0.0", "wasi_command@1.0.0", "wasi@1.0", "wasi@1..0", "wasi@1.x.0", "bad\0id@1.0.0" })
        {
            data.Add(valid with { ExecutionContract = identifier! });
            data.Add(valid with { RequiredImports = [import with { Interface = identifier! }] });
        }

        foreach (var name in new[]
                 {
                     null, "", " ", "Get", "bad_name", "bad\0name",
                     "[method]descriptor", "[method].read", "[method]descriptor.",
                     "[method]descriptor.read.more", "[static]Descriptor.open",
                     "[constructor]descriptor.open", "[resource-drop]",
                     "[resource-drop]Descriptor", "[export-resource-new]descriptor.more",
                 })
        {
            data.Add(valid with { RequiredImports = [import with { Name = name! }] });
        }

        return data;
    }

    private static DeploymentArtifact TimeZoneArtifact(DeploymentManifest manifest) => new(
        manifest.Artifacts.Single(artifact => artifact.Role == "application").RelativePath + ".tz-info",
        "timezone-data",
        "application/octet-stream",
        new string('e', 64),
        1);
}
