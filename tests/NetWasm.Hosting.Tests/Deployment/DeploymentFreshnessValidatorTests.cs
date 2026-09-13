using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class DeploymentFreshnessValidatorTests
{
    [Fact]
    public void ReturnsManifestWhenDescriptorBytesAndCompleteArtifactClosureMatch()
    {
        var (subject, request, manifest) = CreateSubject();
        Assert.Same(manifest, subject.Validate(request));
    }

    [Fact]
    public void ValidatesDescriptorBeforeReadingManifest()
    {
        var manifest = ManifestFixture.Create();
        var descriptor = ManifestFixture.CreateDescriptor(manifest, new string('f', 64));
        var failure = new ArgumentException("Rejected descriptor.");
        var subject = new DeploymentFreshnessValidator(
            new ExecutionDescriptorValidationStub(value =>
            {
                Assert.Same(descriptor, value);
                throw failure;
            }),
            new ManifestReaderStub(_ =>
            {
                Assert.Fail("Must not read manifest.");
                return null!;
            }),
            new ContentHasherStub(_ =>
            {
                Assert.Fail("Must not hash manifest.");
                return null!;
            }));
        var request = new DeploymentFreshnessRequest(descriptor, new byte[] { 1 }, []);
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Validate(request)));
    }

    [Fact]
    public void PropagatesManifestReadFailureBeforeHashing()
    {
        var manifest = ManifestFixture.Create();
        var descriptor = ManifestFixture.CreateDescriptor(manifest, new string('f', 64));
        var failure = new ArgumentException("Rejected manifest.");
        var subject = new DeploymentFreshnessValidator(
            new ExecutionDescriptorValidationStub(_ => { }),
            new ManifestReaderStub(bytes =>
            {
                Assert.Equal(new byte[] { 1 }, bytes.ToArray());
                throw failure;
            }),
            new ContentHasherStub(_ =>
            {
                Assert.Fail("Must not hash rejected manifest.");
                return null!;
            }));
        var request = new DeploymentFreshnessRequest(descriptor, new byte[] { 1 }, []);
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Validate(request)));
    }

    [Fact]
    public void RejectsNullRequest() => Assert.Throws<ArgumentNullException>(() => CreateSubject().Subject.Validate(null!));

    [Fact]
    public void RejectsEmptyManifestBeforeReadingIt()
    {
        var manifest = ManifestFixture.Create();
        var descriptor = ManifestFixture.CreateDescriptor(manifest, new string('f', 64));
        var subject = new DeploymentFreshnessValidator(
            new ExecutionDescriptorValidationStub(_ => { }),
            new ManifestReaderStub(_ =>
            {
                Assert.Fail("Must not read empty manifest.");
                return null!;
            }),
            new ContentHasherStub(_ =>
            {
                Assert.Fail("Must not hash empty manifest.");
                return null!;
            }));
        Assert.Throws<ArgumentException>(() => subject.Validate(new(descriptor, ReadOnlyMemory<byte>.Empty, [])));
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("fingerprint")]
    [InlineData("hosting")]
    public void RejectsStaleDeploymentIdentity(string mutation)
    {
        var (subject, request, _) = CreateSubject();
        request = mutation switch
        {
            "manifest" => request with { ExecutionDescriptor = request.ExecutionDescriptor with { DeploymentManifestSha256 = new string('f', 64) } },
            "fingerprint" => request with { ExecutionDescriptor = request.ExecutionDescriptor with { BuildFingerprint = new string('f', 64) } },
            "hosting" => request with { ExecutionDescriptor = request.ExecutionDescriptor with { HostingVersion = "other" } },
            _ => throw new InvalidOperationException(),
        };
        Assert.Throws<ArgumentException>(() => subject.Validate(request));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("null")]
    [InlineData("null-path")]
    [InlineData("empty-path")]
    [InlineData("null-hash")]
    [InlineData("empty-hash")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-path")]
    [InlineData("wrong-hash")]
    public void RejectsIncompleteAmbiguousOrChangedArtifactClosure(string mutation)
    {
        var (subject, request, _) = CreateSubject();
        var snapshots = request.Artifacts.ToArray();
        request = request with
        {
            Artifacts = mutation switch
            {
                "default" => default,
                "null" => [null!, snapshots[1]],
                "null-path" => [snapshots[0] with { RelativePath = null! }, snapshots[1]],
                "empty-path" => [snapshots[0] with { RelativePath = " " }, snapshots[1]],
                "null-hash" => [snapshots[0] with { Sha256 = null! }, snapshots[1]],
                "empty-hash" => [snapshots[0] with { Sha256 = "" }, snapshots[1]],
                "duplicate" => [snapshots[0], snapshots[0]],
                "missing" => [snapshots[0]],
                "extra" => [.. snapshots, new("extra.js", new string('f', 64))],
                "wrong-path" => [snapshots[0] with { RelativePath = "other.wasm" }, snapshots[1]],
                "wrong-hash" => [snapshots[0] with { Sha256 = new string('f', 64) }, snapshots[1]],
                _ => throw new InvalidOperationException(),
            },
        };
        Assert.ThrowsAny<ArgumentException>(() => subject.Validate(request));
    }

    [Theory]
    [InlineData("descriptor")]
    [InlineData("reader")]
    [InlineData("hasher")]
    public void RejectsMissingDependency(string dependency)
    {
        var descriptorValidator = dependency == "descriptor" ? null : new ExecutionDescriptorValidator();
        var reader = dependency == "reader" ? null : new DeploymentManifestReader(new DeploymentManifestValidator());
        var hasher = dependency == "hasher" ? null : new Sha256ContentHasher();
        Assert.Throws<ArgumentNullException>(() => new DeploymentFreshnessValidator(descriptorValidator!, reader!, hasher!));
    }

    private static (IDeploymentFreshnessValidator Subject, DeploymentFreshnessRequest Request, DeploymentManifest Manifest) CreateSubject()
    {
        var manifest = ManifestFixture.Create();
        var writer = new DeploymentManifestWriter(new DeploymentManifestValidator());
        var bytes = writer.Write(manifest);
        var hasher = new Sha256ContentHasher();
        var descriptor = ManifestFixture.CreateDescriptor(manifest, hasher.Hash(bytes));
        var reader = new ManifestReaderStub(value =>
        {
            var parsed = new DeploymentManifestReader(new DeploymentManifestValidator()).Read(value);
            Assert.Equal(manifest.SemanticBuildId, parsed.SemanticBuildId);
            return manifest;
        });
        var subject = Assert.IsAssignableFrom<IDeploymentFreshnessValidator>(new DeploymentFreshnessValidator(
            new ExecutionDescriptorValidator(), reader, hasher));
        return (subject, new(descriptor, bytes, [.. ManifestFixture.CreateSnapshots(manifest)]), manifest);
    }
}
