using System.Text.Json;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class SdkProvenanceManifestTests
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void BuilderProducesStableSortedContentAndSourceDigestsWithoutSelfHash()
    {
        var reader = new InMemoryPackageFileReader(new Dictionary<string, byte[]>
        {
            ["b"] = [2, 3],
            ["a"] = [1],
            ["source"] = [4, 5]
        });
        var builder = CreateBuilder<ISdkProvenanceManifestBuilder>(new SdkProvenanceManifestBuilder(reader));
        var input = new SdkProvenanceManifestInput(
            "NetWasm.Sdk",
            "0.1.0-preview.1",
            Revision,
            "provenance/NetWasm.Sdk.provenance.json",
            [
                new SdkProvenanceFileInput("b", "Sdk/Sdk.targets"),
                new SdkProvenanceFileInput("a", "README.md")
            ],
            [new SdkProvenanceFileInput("source", "Sdk/Sdk.props", "src/NetWasm.Sdk/Sdk/Sdk.props")]);

        var first = builder.Build(input);
        Assert.Equal(first, builder.Build(input));
        var text = System.Text.Encoding.UTF8.GetString(first);
        Assert.Contains('\n', text);
        Assert.DoesNotContain('\r', text);
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Revision, root.GetProperty("sourceRevision").GetString());
        Assert.Equal("excluded-from-content-digests", root.GetProperty("self").GetString());
        Assert.False(root.GetProperty("self").ValueKind == JsonValueKind.Object);
        Assert.Equal("README.md", root.GetProperty("packageEntries")[0].GetProperty("path").GetString());
        Assert.Equal("src/NetWasm.Sdk/Sdk/Sdk.props", root.GetProperty("sourceEntries")[0].GetProperty("path").GetString());
        Assert.Equal(64, root.GetProperty("packageContentDigest").GetString()!.Length);
        Assert.Equal(64, root.GetProperty("sourceContentDigest").GetString()!.Length);
        Assert.Equal("6be1f9a3be19f3cb21373300ac636da7fe0f23c57a8b470f657f32abc742a921", root.GetProperty("packageContentDigest").GetString());
        Assert.Equal("5f0b4dd8eac129b73e6107b1cb1865b19463ca1ba832bfc96974b2d5498c21b3", root.GetProperty("sourceContentDigest").GetString());
    }

    [Fact]
    public void BuilderRejectsMissingInputsDuplicatePathsAndUnsafePaths()
    {
        Assert.Throws<ArgumentNullException>(() => new SdkProvenanceManifestBuilder(null!));
        var builder = CreateBuilder<ISdkProvenanceManifestBuilder>(new SdkProvenanceManifestBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["a"] = [1] })));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("", "1.0.0", Revision, "manifest.json", [new("a", "a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "", Revision, "manifest.json", [new("a", "a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", "", "manifest.json", [new("a", "a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", "short", "manifest.json", [new("a", "a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK002, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision[..^1] + "g", "manifest.json", [new("a", "a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "/manifest.json", [new("a", "a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "manifest.json", [], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK011, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "manifest.json", [new("a", "a")], []))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "manifest.json", [new("a", "a"), new("a", "A")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK008, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "manifest.json", [new("a", "a")], [new("a", "a", "source"), new("a", "A", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "manifest.json", [new("a", "../a")], [new("a", "a", "source")]))).Code);
        Assert.Equal(NetWasmPackErrorCode.NWPK009, Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput("id", "1.0.0", Revision, "manifest.json", [new("a", "a")], [new("a", "a", "../source")]))).Code);
    }

    [Fact]
    public void BuilderRejectsDuplicatePackagePathsIndependentOfLogicalSourcePaths()
    {
        var builder = CreateBuilder<ISdkProvenanceManifestBuilder>(new SdkProvenanceManifestBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]>
        {
            ["first"] = [1],
            ["second"] = [2]
        })));

        var exception = Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput(
            "id",
            "1.0.0",
            Revision,
            "manifest.json",
            [new("first", "first")],
            [
                new("first", "shared", "source/first"),
                new("second", "SHARED", "source/second")
            ])));

        Assert.Equal(NetWasmPackErrorCode.NWPK008, exception.Code);
    }

    [Fact]
    public void BuilderRejectsEveryUnsafeManifestOrLogicalPathShape()
    {
        var builder = CreateBuilder<ISdkProvenanceManifestBuilder>(new SdkProvenanceManifestBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["a"] = [1] })));
        foreach (var path in new[] { "", " ", "\\manifest", "/manifest", "C:/manifest", ".", "..", "a//b", "a/./b", "a/../b", "a\u001fb" })
        {
            var exception = Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput(
                "id",
                "1.0.0",
                Revision,
                path,
                [new("a", "a")],
                [new("a", "a", "source")])));
            Assert.Equal(NetWasmPackErrorCode.NWPK009, exception.Code);
        }

        var nullLogicalPath = Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput(
            "id",
            "1.0.0",
            Revision,
            "manifest.json",
            [new("a", "a")],
            [new("a", "a")])));
        Assert.Equal(NetWasmPackErrorCode.NWPK009, nullLogicalPath.Code);
    }

    [Fact]
    public void BuilderSurfacesUnreadableSourceAndNullFileEntries()
    {
        var builder = CreateBuilder<ISdkProvenanceManifestBuilder>(new SdkProvenanceManifestBuilder(new InMemoryPackageFileReader(new Dictionary<string, byte[]> { ["a"] = [1] })));
        var unreadable = Assert.Throws<NetWasmPackException>(() => builder.Build(new SdkProvenanceManifestInput(
            "id",
            "1.0.0",
            Revision,
            "manifest.json",
            [new("missing", "a")],
            [new("a", "a", "source")])));
        Assert.Equal(NetWasmPackErrorCode.NWPK011, unreadable.Code);

        Assert.Throws<ArgumentNullException>(() => builder.Build(new SdkProvenanceManifestInput(
            "id",
            "1.0.0",
            Revision,
            "manifest.json",
            null!,
            [new("a", "a", "source")])));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new SdkProvenanceManifestInput(
            "id",
            "1.0.0",
            Revision,
            "manifest.json",
            [new("a", "a")],
            null!)));
        Assert.Throws<ArgumentNullException>(() => builder.Build(new SdkProvenanceManifestInput(
            "id",
            "1.0.0",
            Revision,
            "manifest.json",
            [null!],
            [new("a", "a", "source")])));
    }

    [Fact]
    public void MsBuildTaskWritesManifestAndHandlesInvalidOutput()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "obj", "provenance.json");
        var builder = new RecordingProvenanceBuilder();
        Assert.NotNull(new SdkProvenanceManifestMsBuildTask());
        Assert.Throws<ArgumentNullException>(() => new SdkProvenanceManifestMsBuildTask(null!));
        var configuredTask = new SdkProvenanceManifestMsBuildTask(builder)
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            PackageId = "NetWasm.Sdk",
            PackageVersion = "1.0.0",
            SourceRevision = Revision,
            ManifestPackagePath = "provenance/manifest.json",
            ManifestOutputPath = output,
            PackageFiles = [FileItem("a", "a")],
            SourceFiles = [FileItem("source", "a", "src/a")]
        };
        Assert.True(((ITask)configuredTask).Execute());
        Assert.True(builder.Called);
        Assert.Equal("manifest", File.ReadAllText(output));
        Assert.Equal("src/a", builder.Input!.SourceFiles.Single().LogicalSourcePath);

        var configuredInvalidTask = new SdkProvenanceManifestMsBuildTask(builder)
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            ManifestOutputPath = "manifest.json",
            PackageFiles = [FileItem("a", "a")],
            SourceFiles = [FileItem("source", "a", "src/a")]
        };
        Assert.False(((ITask)configuredInvalidTask).Execute());

        var configuredThrowingTask = new SdkProvenanceManifestMsBuildTask(new ThrowingProvenanceBuilder())
        {
            BuildEngine = new PackTestFixtures.RecordingBuildEngine(),
            ManifestOutputPath = output,
            PackageFiles = [FileItem("a", "a")],
            SourceFiles = [FileItem("source", "a", "src/a")]
        };
        Assert.False(((ITask)configuredThrowingTask).Execute());
    }

    private static TaskItem FileItem(string source, string package, string? logical = null)
    {
        var item = new TaskItem(source);
        item.SetMetadata("SourcePath", source);
        item.SetMetadata("PackagePath", package);
        if (logical is not null)
        {
            item.SetMetadata("LogicalSourcePath", logical);
        }

        return item;
    }

    private static T CreateBuilder<T>(T builder) where T : ISdkProvenanceManifestBuilder => builder;

    private sealed class RecordingProvenanceBuilder : ISdkProvenanceManifestBuilder
    {
        public bool Called { get; private set; }
        public SdkProvenanceManifestInput? Input { get; private set; }

        public byte[] Build(SdkProvenanceManifestInput input)
        {
            Called = true;
            Input = input;
            return "manifest"u8.ToArray();
        }
    }

    private sealed class ThrowingProvenanceBuilder : ISdkProvenanceManifestBuilder
    {
        public byte[] Build(SdkProvenanceManifestInput input) => throw new InvalidOperationException("private source path");
    }
}
