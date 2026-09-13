using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class DeploymentManifestWriterTests
{
    [Fact]
    public void WritesDeterministicPublishSafeUtf8AndRoundTrips()
    {
        var manifest = ManifestFixture.Create();
        var subject = Assert.IsAssignableFrom<IDeploymentManifestWriter>(new DeploymentManifestWriter(new DeploymentManifestValidator()));
        var bytes = subject.Write(manifest);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(bytes, subject.Write(manifest));
        Assert.StartsWith("{\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'{', bytes[0]);
        Assert.DoesNotContain(Path.GetFullPath("."), text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("component", json.RootElement.GetProperty("deploymentKind").GetString());
        Assert.False(json.RootElement.TryGetProperty("grants", out _));
        var restored = new DeploymentManifestReader(new DeploymentManifestValidator()).Read(bytes);
        Assert.Equal(manifest.SemanticBuildId, restored.SemanticBuildId);
        Assert.Equal(manifest.Versions, restored.Versions);
        Assert.Equal<string>(manifest.RuntimeFeatures, restored.RuntimeFeatures);
        Assert.Equal<DeploymentArtifact>(manifest.Artifacts, restored.Artifacts);
        Assert.Equal<string>(manifest.RequiredImportModules, restored.RequiredImportModules);
        ManifestFixture.AssertFunctionsEqual(manifest.RequiredImports, restored.RequiredImports);
        ManifestFixture.AssertFunctionsEqual(manifest.Exports, restored.Exports);
    }

    [Fact]
    public void CanonicalizesCollectionOrderWithoutMutatingInput()
    {
        var manifest = ManifestFixture.Create();
        var artifacts = manifest.Artifacts.Reverse().ToArray();
        var imports = new[]
        {
            manifest.RequiredImports[0] with { Interface = "wasi:filesystem/types@0.2.11", Name = "z-last" },
            manifest.RequiredImports[0] with { Interface = "wasi:cli/environment@0.2.11", Name = "z-last" },
            manifest.RequiredImports[0],
        };
        var exports = new[]
        {
            manifest.Exports[0] with { Name = "z-last" },
            manifest.Exports[0],
        };
        manifest = manifest with
        {
            RuntimeFeatures = ["local-time"],
            Artifacts = [.. artifacts],
            RequiredImportModules = [
                "wasi:filesystem/types@0.2.11",
                "wasi:cli/environment@0.2.11",
            ],
            RequiredImports = [.. imports],
            Exports = [.. exports],
        };
        var canonical = manifest with
        {
            RuntimeFeatures = ["local-time"],
            Artifacts = [.. artifacts.Reverse()],
            RequiredImportModules = [
                "wasi:cli/environment@0.2.11",
                "wasi:filesystem/types@0.2.11",
            ],
            RequiredImports = [imports[2], imports[1], imports[0]],
            Exports = [exports[1], exports[0]],
        };
        var subject = new DeploymentManifestWriter(new DeploymentManifestValidator());
        Assert.Equal(subject.Write(canonical), subject.Write(manifest));
        Assert.Same(artifacts[0], manifest.Artifacts[0]);
        Assert.Equal("local-time", manifest.RuntimeFeatures[0]);
        Assert.Same(imports[0], manifest.RequiredImports[0]);
        Assert.Same(exports[0], manifest.Exports[0]);
    }

    [Fact]
    public void PropagatesValidationFailureBeforeSerializing()
    {
        var manifest = ManifestFixture.Create();
        var failure = new ArgumentException("Rejected manifest.");
        var subject = new DeploymentManifestWriter(new ManifestValidationStub(value =>
        {
            Assert.Same(manifest, value);
            throw failure;
        }));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Write(manifest)));
    }

    [Fact]
    public void RejectsNullInputBeforeDelegation()
    {
        var subject = new DeploymentManifestWriter(new ManifestValidationStub(_ => Assert.Fail("Must not delegate.")));
        Assert.Throws<ArgumentNullException>(() => subject.Write(null!));
    }

    [Fact]
    public void RejectsMissingValidator() => Assert.Throws<ArgumentNullException>(() => new DeploymentManifestWriter(null!));
}
