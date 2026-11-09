using System.Text.Json;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimePackManifestParserTests
{
    private static IRuntimePackManifestParser Create() =>
        Assert.IsAssignableFrom<IRuntimePackManifestParser>(new RuntimePackManifestParser());

    [Fact]
    public void RejectsMalformedToolchainVersion() =>
        Assert.Equal("The NetWasm runtime pack Emscripten requirement is invalid.",
            ReadInvalid(RuntimePackTestData.Manifest() with { EmscriptenVersion = "invalid" }).Message);

    [Theory]
    [InlineData("")]
    [InlineData("outside/libc.a")]
    public void RejectsInvalidSystemLibraryName(string name)
    {
        var target = RuntimePackTestData.Target("wasm32");
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = ReplaceTarget(target with { SystemLibraries = target.SystemLibraries with { Names = [name] } }),
        };
        Assert.Equal("The NetWasm runtime link closure is empty.", ReadInvalid(manifest).Message);
    }

    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "NetWasm")]
    [InlineData("wasm32", "Bump")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "NetWasm")]
    [InlineData("wasm64", "Bump")]
    public void ValidatesSpeedAssetsForEveryTargetAndCollector(string target, string collector)
    {
        var source = RuntimePackTestData.Manifest();
        var index = source.Targets.IndexOf(source.TargetLookup[(target, collector)]);
        var original = source.Targets[index];
        var speed = original.Speed! with { RuntimeFootprintBytes = original.RuntimeFootprintBytes + 8192 };
        var valid = source with { Targets = source.Targets.SetItem(index, original with { Speed = speed }) };
        var parsed = Create().Parse(JsonSerializer.Serialize(valid));
        Assert.Equal(speed, parsed.TargetLookup[(target, collector)].Speed);

        RuntimePackSpeedAssets?[] invalid =
        [
            null,
            speed with { RuntimeFootprintBytes = original.NativeStackSizeBytes - 1 },
            speed with { RuntimeArchive = original.RuntimeArchive },
            speed with { CollectorArchive = original.CollectorArchive },
            speed with { RuntimeArchive = null! },
            speed with { CollectorArchive = null! },
            speed with { RuntimeArchive = speed.RuntimeArchive with { Sha256 = null! } },
            speed with { CollectorArchive = speed.CollectorArchive with { Sha256 = "bad" } },
        ];
        foreach (var assets in invalid)
        {
            var malformed = source with { Targets = source.Targets.SetItem(index, original with { Speed = assets }) };
            var error = Record.Exception(() => Create().Parse(JsonSerializer.Serialize(malformed)));
            Assert.True(error is InvalidOperationException or ArgumentNullException);
        }
    }

    // Each target and its speed variant name the WASI platform archive and its replacement for a
    // plain core module, at their fixed paths.
    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm64", "Bump")]
    public void ValidatesPlatformArchivesOfTheTargetAndItsSpeedVariant(string target, string collector)
    {
        var source = RuntimePackTestData.Manifest();
        var index = source.Targets.IndexOf(source.TargetLookup[(target, collector)]);
        var original = source.Targets[index];
        var parsed = Create().Parse(JsonSerializer.Serialize(source));
        Assert.Equal(original.PlatformArchives, parsed.TargetLookup[(target, collector)].PlatformArchives);
        Assert.Equal(original.Speed!.PlatformArchives, parsed.TargetLookup[(target, collector)].Speed!.PlatformArchives);

        var platform = original.PlatformArchives!;
        RuntimePackTarget[] invalid =
        [
            original with { PlatformArchives = null },
            original with { PlatformArchives = platform with { Wasi = platform.NoWasi } },
            original with { PlatformArchives = platform with { NoWasi = platform.Wasi } },
            original with { PlatformArchives = platform with { NoWasi = platform.NoWasi with { Sha256 = "bad" } } },
            original with { Speed = original.Speed with { PlatformArchives = null } },
            original with { Speed = original.Speed with { PlatformArchives = platform } },
        ];
        foreach (var malformed in invalid)
        {
            var manifest = source with { Targets = source.Targets.SetItem(index, malformed) };
            var error = Record.Exception(() => Create().Parse(JsonSerializer.Serialize(manifest)));
            Assert.True(error is InvalidOperationException or ArgumentNullException, error?.ToString());
        }
    }

    [Fact]
    public void ReadsGeneratedRuntimePackManifestShape()
    {
        var manifest = Create().Parse(
            JsonSerializer.Serialize(RuntimePackTestData.Manifest()));

        Assert.Equal(6, manifest.SchemaVersion);
        Assert.Equal("Compact", manifest.DefaultGarbageCollector);
        Assert.Equal(6, manifest.TargetLookup.Count);
        Assert.Equal("netwasm.runtime.v1", manifest.RuntimeAbi);
        Assert.Equal("6.0.7", manifest.EmscriptenVersion);
        Assert.Equal(65_536, manifest.WasmPageSize);
        Assert.Equal(2, manifest.Exports.Length);
        var inlineIntent = Assert.Single(manifest.InlineIntents);
        Assert.Equal("initialize", inlineIntent.Export);
        Assert.Equal(RuntimeFunctionInlinePreference.Aggressive, inlineIntent.Preference);
        Assert.Equal(["wasm32", "wasm64", "wasm32", "wasm64", "wasm32", "wasm64"], manifest.Targets.Select(static target => target.Target));
        Assert.Equal([4, 8, 4, 8, 4, 8], manifest.Targets.Select(static target => target.PointerSizeBytes));
        Assert.Equal([91_968, 114_352, 91_968, 114_352, 91_968, 114_352], manifest.Targets.Select(static target => target.RuntimeFootprintBytes));
        foreach (var target in manifest.Targets)
        {
            var profile = Assert.IsType<RuntimeNativeValidationProfile>(target.NativeValidation);
            Assert.Equal(1, profile.Version);
            Assert.Equal(RuntimePackTestData.NativeProfile(target.Target).Features.ToArray(), profile.Features.ToArray());
            var contract = Assert.Single(profile.Imports);
            Assert.Equal("env", contract.Module);
            Assert.Equal("emscripten_notify_memory_growth", contract.Name);
            Assert.Equal(target.PointerSizeBytes == 8 ? (byte)0x7e : (byte)0x7f, Assert.Single(contract.Parameters));
            Assert.Empty(contract.Results);
            Assert.True(contract.Required);
            Assert.Contains("libc.a", target.SystemLibraries.Names);
            Assert.Equal($"{target.Target}/{target.GarbageCollector.ToLowerInvariant()}/libgc.a", target.CollectorArchive.Path);
            Assert.Equal($"{target.Target}/allowed-undefined-symbols.txt",
                target.AllowedUndefinedSymbols.Path);
            Assert.Equal($"{target.Target}/system-libraries/libc.a",
                Assert.Single(target.SystemLibraries.Assets).Path);
        }
    }

    [Fact]
    public void RuntimeOnlySchemaSixPacksNeedNotDeclareNativeValidation()
    {
        var source = RuntimePackTestData.Manifest();
        var manifest = Create().Parse(JsonSerializer.Serialize(source with
        { Targets = [.. source.Targets.Select(target => target with { NativeValidation = null })] }));

        Assert.All(manifest.Targets, target => Assert.Null(target.NativeValidation));
        Assert.Equal(source.RuntimeAbi, manifest.RuntimeAbi);
    }

    [Fact]
    public void RejectsEmptyAndMalformedManifest()
    {
        var parser = Create();
        Assert.Equal(
            "The NetWasm runtime pack manifest is malformed.",
            Assert.Throws<InvalidOperationException>(() => parser.Parse("{")).Message);
        Assert.Throws<ArgumentException>(() => parser.Parse(" "));
        Assert.Throws<ArgumentNullException>(() => parser.Parse(null!));
    }

    [Fact]
    public void RejectsUnsupportedSchema()
    {
        foreach (var manifest in InvalidSchemaManifests())
        {
            var exception = Assert.Throws<InvalidOperationException>(() => Create().Parse(JsonSerializer.Serialize(manifest)));
            Assert.Equal("The NetWasm runtime pack manifest schema is unsupported.", exception.Message);
        }
    }

    private static RuntimePackManifest?[] InvalidSchemaManifests() =>
    [
        null,
        RuntimePackTestData.Manifest() with { SchemaVersion = 1 },
        RuntimePackTestData.Manifest() with { SchemaVersion = 4 },
        RuntimePackTestData.Manifest() with { SchemaVersion = 5 },
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
    public void RejectsInvalidInlineIntentContract()
    {
        foreach (var manifest in InvalidInlineIntentManifests())
        {
            var exception = ReadInvalid(manifest);
            Assert.Equal(
                "The NetWasm runtime inline-intent contract is invalid.",
                exception.Message);
        }
    }

    private static RuntimePackManifest[] InvalidInlineIntentManifests() =>
    [
        RuntimePackTestData.Manifest() with { InlineIntents = [null!] },
        RuntimePackTestData.Manifest() with
        {
            InlineIntents = [new(" ", RuntimeFunctionInlinePreference.Aggressive)],
        },
        RuntimePackTestData.Manifest() with
        {
            InlineIntents = [new("initialize", (RuntimeFunctionInlinePreference)42)],
        },
        RuntimePackTestData.Manifest() with
        {
            InlineIntents = [new("missing", RuntimeFunctionInlinePreference.Aggressive)],
        },
        RuntimePackTestData.Manifest() with
        {
            InlineIntents =
            [
                new("initialize", RuntimeFunctionInlinePreference.Aggressive),
                new("initialize", RuntimeFunctionInlinePreference.Never),
            ],
        },
    ];

    [Fact]
    public void RejectsNullInlineIntentCollection()
    {
        var json = JsonSerializer.Serialize(
            RuntimePackTestData.Manifest() with { InlineIntents = [] });
        var invalid = json.Replace(
            "\"InlineIntents\":[]",
            "\"InlineIntents\":null",
            StringComparison.Ordinal);
        Assert.NotEqual(json, invalid);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Create().Parse(invalid));

        Assert.Equal(
            "The NetWasm runtime pack manifest is malformed.",
            exception.Message);
    }

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
                Targets = ReplaceTarget(target),
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
            Targets = ReplaceTarget(unsupported),
        };
        Assert.Equal(
            "The NetWasm runtime target is unsupported.",
            ReadInvalid(manifest).Message);
    }

    [Fact]
    public void RejectsEmptyLinkClosure()
    {
        var target = RuntimePackTestData.Target("wasm32") with
        {
            SystemLibraries = new([], []),
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = ReplaceTarget(target),
        };
        Assert.Equal("The NetWasm runtime link closure is empty.", ReadInvalid(manifest).Message);
    }

    [Fact]
    public void RejectsSystemLibraryOutsideThePackagedClosure()
    {
        var target = RuntimePackTestData.Target("wasm32");
        var wrong = target with
        {
            SystemLibraries = target.SystemLibraries with
            {
                Assets = [RuntimePackTestData.Asset("wasm32/other/libc.a")],
            },
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = ReplaceTarget(wrong),
        };

        Assert.Equal("The NetWasm runtime system-library path is invalid.",
            ReadInvalid(manifest).Message);
    }

    [Theory]
    [InlineData("runtime", "wasm32/renamed-runtime.a")]
    [InlineData("collector", "wasm32/renamed-collector.a")]
    [InlineData("allowed", "wasm32/renamed-symbols.txt")]
    public void RejectsRenamedTargetAsset(string asset, string path)
    {
        var target = RuntimePackTestData.Target("wasm32");
        target = asset switch
        {
            "runtime" => target with { RuntimeArchive = RuntimePackTestData.Asset(path) },
            "collector" => target with { CollectorArchive = RuntimePackTestData.Asset(path) },
            _ => target with { AllowedUndefinedSymbols = RuntimePackTestData.Asset(path) },
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = ReplaceTarget(target),
        };

        Assert.Equal("The NetWasm runtime target asset path is invalid.",
            ReadInvalid(manifest).Message);
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
            Targets = ReplaceTarget(target),
        };
        Assert.Equal(
            "The NetWasm runtime pack contains an invalid asset descriptor.",
            ReadInvalid(manifest).Message);
    }

    [Theory]
    [InlineData("short")]
    [InlineData(null)]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void RejectsInvalidAssetDigest(string? digest)
    {
        var target = RuntimePackTestData.Target("wasm32") with
        {
            RuntimeArchive = RuntimePackTestData.Asset("wasm32/runtime.a") with { Sha256 = digest! },
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            Targets = ReplaceTarget(target),
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

    private static System.Collections.Immutable.ImmutableArray<RuntimePackTarget> ReplaceTarget(RuntimePackTarget target) =>
        [target, RuntimePackTestData.Target("wasm64"), RuntimePackTestData.Target("wasm32", "NetWasm"),
            RuntimePackTestData.Target("wasm64", "NetWasm"), RuntimePackTestData.Target("wasm32", "Bump"),
            RuntimePackTestData.Target("wasm64", "Bump")];

    private static void Read(RuntimePackManifest manifest)
    {
        Create().Parse(JsonSerializer.Serialize(manifest));
    }
}
