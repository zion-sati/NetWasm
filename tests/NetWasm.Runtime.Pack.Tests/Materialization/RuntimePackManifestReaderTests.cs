// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimePackManifestReaderTests
{
    [Fact]
    public void DelegatesFileContentsToParser()
    {
        using var directory = new TemporaryDirectory();
        var parser = new RecordingParser();
        var reader = Assert.IsAssignableFrom<IRuntimePackManifestReader>(new RuntimePackManifestReader(parser));

        Assert.Same(parser.Manifest, reader.Read(directory.Write("manifest.json", "contents")));
        Assert.Equal("contents", parser.Json);
    }

    [Fact]
    public void RejectsMissingPathBeforeParsing()
    {
        using var directory = new TemporaryDirectory();
        var parser = new RecordingParser();
        var reader = Assert.IsAssignableFrom<IRuntimePackManifestReader>(new RuntimePackManifestReader(parser));

        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
        Assert.Equal("The NetWasm runtime pack manifest is missing.",
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory.PathTo("missing.json"))).Message);
        Assert.Null(parser.Json);
    }

    [Fact]
    public void RejectsMissingParser() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimePackManifestReader(null!));

    private sealed class RecordingParser : IRuntimePackManifestParser
    {
        public RuntimePackManifest Manifest { get; } = RuntimePackTestData.Manifest();
        public string? Json { get; private set; }

        public RuntimePackManifest Parse(string json)
        {
            Json = json;
            return Manifest;
        }
    }
}
