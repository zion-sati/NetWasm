// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeArtifactWriterTests
{
    [Fact]
    public void AtomicallyWritesMissingAndExistingArtifacts()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathTo("owned/runtime.wasm");
        var writer = Assert.IsAssignableFrom<IRuntimeArtifactWriter>(
            new RuntimeArtifactWriter());

        writer.Write(path, new byte[] { 1, 2, 3 });
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));

        writer.Write(path, new byte[] { 4, 5 });
        Assert.Equal([4, 5], File.ReadAllBytes(path));
    }

    [Fact]
    public void FailedReplacementRemovesTemporarySibling()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathTo("owned/runtime.wasm");
        Directory.CreateDirectory(path);
        var writer = new RuntimeArtifactWriter();

        Assert.ThrowsAny<IOException>(() => writer.Write(path, new byte[] { 1 }));

        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(path)!,
            "*.tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingPath(string path)
    {
        var writer = new RuntimeArtifactWriter();

        Assert.Throws<ArgumentException>(() => writer.Write(path, ReadOnlyMemory<byte>.Empty));
    }
}
