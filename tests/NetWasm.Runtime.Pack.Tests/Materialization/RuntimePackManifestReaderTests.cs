using System.Text.Json;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimePackManifestReaderTests
{
    [Fact]
    public void ReadsCheckedInRuntimePackManifest()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/NetWasm.Runtime.Pack/runtime/runtime-pack.json"));

        var reader = Assert.IsAssignableFrom<IRuntimePackManifestReader>(
            new RuntimePackManifestReader());
        var manifest = reader.Read(path);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("netwasm.runtime.v1", manifest.RuntimeAbi);
        Assert.Equal(65_536, manifest.WasmPageSize);
        Assert.Equal(83, manifest.Exports.Length);
        Assert.Equal(["wasm32", "wasm64"], manifest.Targets.Select(static target => target.Target));
        Assert.Equal([4, 8], manifest.Targets.Select(static target => target.PointerSizeBytes));
        Assert.Equal([94_432, 117_488], manifest.Targets.Select(static target => target.RuntimeFootprintBytes));
        foreach (var target in manifest.Targets)
        {
            Assert.Contains(target.LinkInputs,
                input => input.Path == $"{target.Target}/system/libstandalonewasm-nocatch-memgrow.a");
        }
    }

    [Fact]
    public void RejectsMissingAndMalformedManifest()
    {
        using var directory = new TemporaryDirectory();
        var reader = new RuntimePackManifestReader();
        Assert.Equal(
            "The NetWasm runtime pack manifest is missing.",
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory.PathTo("missing.json"))).Message);
        Assert.Equal(
            "The NetWasm runtime pack manifest is malformed.",
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory.Write("manifest.json", "{"))).Message);
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
    }

    [Fact]
    public void RejectsUnsupportedSchema()
    {
        foreach (var manifest in InvalidSchemaManifests())
        {
            using var directory = new TemporaryDirectory();
            var path = directory.Write("manifest.json", JsonSerializer.Serialize(manifest));
            var exception = Assert.Throws<InvalidOperationException>(() => new RuntimePackManifestReader().Read(path));
            Assert.Equal("The NetWasm runtime pack manifest schema is unsupported.", exception.Message);
        }
    }

    private static RuntimePackManifest?[] InvalidSchemaManifests() =>
    [
        null,
        RuntimePackTestData.Manifest() with { SchemaVersion = 2 },
        RuntimePackTestData.Manifest() with { WasmPageSize = 1 },
    ];

    [Fact]
    public void RejectsInvalidExportContract()
    {
        foreach (var manifest in InvalidExportManifests())
        {
            var exception = ReadInvalid(manifest);
            Assert.Equal("The NetWasm runtime export contract is invalid.", exception.Message);
        }
    }

    private static RuntimePackManifest[] InvalidExportManifests() =>
    [
        RuntimePackTestData.Manifest() with { Exports = [] },
        RuntimePackTestData.Manifest() with { Exports = [" "] },
        RuntimePackTestData.Manifest() with { Exports = ["allocate", "allocate"] },
    ];

    [Fact]
    public void RejectsInvalidTargetContract()
    {
        foreach (var manifest in InvalidTargetContractManifests())
        {
            var exception = ReadInvalid(manifest);
            Assert.Equal("The NetWasm runtime target contract is invalid.", exception.Message);
        }
    }

    private static RuntimePackManifest[] InvalidTargetContractManifests() =>
    [
        RuntimePackTestData.Manifest() with { Targets = [RuntimePackTestData.Target("wasm32")] },
        RuntimePackTestData.Manifest() with
        {
            Targets = [RuntimePackTestData.Target("wasm32"), RuntimePackTestData.Target("wasm32")],
        },
    ];

    [Fact]
    public void RejectsInvalidTargetLayout()
    {
        foreach (var target in InvalidTargetLayouts())
        {
            var manifest = RuntimePackTestData.Manifest() with
            {
                Targets = [target, RuntimePackTestData.Target("wasm64")],
            };
            var exception = ReadInvalid(manifest);
            Assert.Equal("The NetWasm runtime target layout is invalid.", exception.Message);
        }
    }

    private static RuntimePackTarget[] InvalidTargetLayouts()
    {
        var target = RuntimePackTestData.Target("wasm32");
        return
        [
            target with { PointerSizeBytes = 8 },
            target with { Alignment = 0 },
            target with { Alignment = 3 },
            target with { RuntimeFootprintBytes = 1 },
            target with { NativeStackSizeBytes = 1 },
            target with { DefaultInitialHeapSizeBytes = -1 },
            target with { DefaultMaximumMemorySizeBytes = 0 },
            target with { DefaultMaximumMemorySizeBytes = target.MaximumMemorySizeBytes + 65_536 },
            target with { MaximumMemorySizeBytes = target.MaximumMemorySizeBytes - 1 },
        ];
    }

    [Fact]
    public void RejectsUnsupportedTarget()
    {
        var unsupported = RuntimePackTestData.Target("wasm32") with { Target = "wasm128" };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = [unsupported, RuntimePackTestData.Target("wasm64")],
        };
        Assert.Equal(
            "The NetWasm runtime target is unsupported.",
            ReadInvalid(manifest).Message);
    }

    [Fact]
    public void RejectsEmptyLinkClosure()
    {
        var target = RuntimePackTestData.Target("wasm32") with { LinkInputs = [] };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = [target, RuntimePackTestData.Target("wasm64")],
        };
        Assert.Equal("The NetWasm runtime link closure is empty.", ReadInvalid(manifest).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute.a")]
    [InlineData("../outside.a")]
    public void RejectsUnsafeAssetPath(string path)
    {
        var target = RuntimePackTestData.Target("wasm32") with
        {
            RuntimeArchive = RuntimePackTestData.Asset(path),
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = [target, RuntimePackTestData.Target("wasm64")],
        };
        Assert.Equal(
            "The NetWasm runtime pack contains an invalid asset descriptor.",
            ReadInvalid(manifest).Message);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void RejectsInvalidAssetDigest(string digest)
    {
        var target = RuntimePackTestData.Target("wasm32") with
        {
            RuntimeArchive = RuntimePackTestData.Asset("wasm32/runtime.a") with { Sha256 = digest },
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = [target, RuntimePackTestData.Target("wasm64")],
        };
        Assert.Equal(
            "The NetWasm runtime pack contains an invalid asset descriptor.",
            ReadInvalid(manifest).Message);
    }

    [Theory]
    [InlineData("BuildSeam")]
    [InlineData("Configuration")]
    [InlineData("ToolchainFile")]
    [InlineData("ToolchainFingerprint")]
    [InlineData("RuntimeSource")]
    [InlineData("LayoutAuthority")]
    public void RejectsMissingProvenance(string property)
    {
        var provenance = RuntimePackTestData.Manifest().Provenance;
        provenance = property switch
        {
            "BuildSeam" => provenance with { BuildSeam = "" },
            "Configuration" => provenance with { Configuration = "" },
            "ToolchainFile" => provenance with { ToolchainFile = "" },
            "ToolchainFingerprint" => provenance with { ToolchainFingerprint = "" },
            "RuntimeSource" => provenance with { RuntimeSource = "" },
            _ => provenance with { LayoutAuthority = "" },
        };
        Assert.Throws<ArgumentException>(() => Read(
            RuntimePackTestData.Manifest() with { Provenance = provenance }));
    }

    private static InvalidOperationException ReadInvalid(RuntimePackManifest manifest)
    {
        return Assert.Throws<InvalidOperationException>(() => Read(manifest));
    }

    private static void Read(RuntimePackManifest manifest)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("manifest.json", JsonSerializer.Serialize(manifest));
        new RuntimePackManifestReader().Read(path);
    }
}
