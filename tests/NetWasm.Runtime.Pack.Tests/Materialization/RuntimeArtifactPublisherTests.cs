using System.Security.Cryptography;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeArtifactPublisherTests
{
    private readonly RuntimeArtifactPublisher _publisher = new(new Sha256ArtifactDigestCalculator());

    [Fact]
    public void PublishesMissingOrChangedOutput()
    {
        using var directory = new TemporaryDirectory();
        var output = directory.PathTo("obj/runtime.wasm");
        var first = new byte[] { 1, 2, 3 };
        var second = new byte[] { 4, 5, 6 };

        _publisher.PublishIfDifferent(output, first, Digest(first));
        Assert.Equal(first, File.ReadAllBytes(output));

        _publisher.PublishIfDifferent(output, second, Digest(second));
        Assert.Equal(second, File.ReadAllBytes(output));
    }

    [Fact]
    public void PreservesIdenticalOutputTimestamp()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 1, 2, 3 };
        var output = directory.WriteBytes("obj/runtime.wasm", bytes);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(output, timestamp);

        _publisher.PublishIfDifferent(output, bytes, Digest(bytes));

        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(output));
    }

    [Fact]
    public void FailedAtomicMoveRemovesTemporarySibling()
    {
        using var directory = new TemporaryDirectory();
        var output = directory.PathTo("obj/runtime.wasm");
        Directory.CreateDirectory(output);
        var bytes = new byte[] { 1, 2, 3 };

        Assert.ThrowsAny<IOException>(() =>
            _publisher.PublishIfDifferent(output, bytes, Digest(bytes)));

        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(output)!, "*.tmp"));
    }

    private static string Digest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
