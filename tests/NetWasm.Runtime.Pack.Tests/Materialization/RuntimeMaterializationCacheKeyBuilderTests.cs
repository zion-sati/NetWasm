using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeMaterializationCacheKeyBuilderTests
{
    private readonly RuntimeMaterializationCacheKeyBuilder _builder = new();

    [Fact]
    public void IgnoresMachinePathsAndUnalignedSourceLayoutChanges()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();

        var firstKey = _builder.Build(Request(first.Path));
        var secondKey = _builder.Build(Request(second.Path));

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void ChangesForEverySemanticInputClass()
    {
        using var directory = new TemporaryDirectory();
        var baseline = Request(directory.Path);
        var baselineKey = _builder.Build(baseline);
        var secondAsset = new RuntimePackAsset(
            "wasm32/system-libraries/libm.a",
            new string('b', 64));
        var orderedTarget = baseline.Target with
        {
            SystemLibraries = new(
                ["libc.a", "libm.a"],
                [baseline.Target.SystemLibraries.Assets[0], secondAsset]),
        };
        var reorderedTarget = orderedTarget with
        {
            SystemLibraries = new(
                ["libm.a", "libc.a"],
                [secondAsset, baseline.Target.SystemLibraries.Assets[0]]),
        };

        RuntimeMaterializationCacheKeyRequest[] changes =
        [
            baseline with { BuildIdentity = baseline.BuildIdentity with { CompilerVersion = "compiler-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { RuntimeVersion = "runtime-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { RuntimePackVersion = "runtime-pack-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmLdVersion = "wasm-ld-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmOptVersion = "wasm-opt-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmToolsVersion = "wasm-tools-2" } },
            baseline with { Manifest = baseline.Manifest with { RuntimeAbi = "netwasm.runtime.v2" } },
            baseline with
            {
                Manifest = baseline.Manifest with
                {
                    Provenance = baseline.Manifest.Provenance with { ToolchainFingerprint = "toolchain-2" },
                },
            },
            baseline with { Layout = baseline.Layout with { RuntimeGlobalBase = 65_568 } },
            baseline with { Layout = baseline.Layout with { InitialMemorySizeBytes = 327_680 } },
            baseline with { Optimization = RuntimeWasmOptimization.O3 },
            baseline with
            {
                Target = baseline.Target with
                {
                    RuntimeArchive = baseline.Target.RuntimeArchive with { Sha256 = new string('c', 64) },
                },
            },
            baseline with { Target = orderedTarget },
            baseline with { Target = reorderedTarget },
            baseline with { LinkArguments = baseline.LinkArguments.Add("--new-link-policy") },
            baseline with { OptimizationArguments = baseline.OptimizationArguments.Add("--new-opt-policy") },
        ];

        Assert.All(changes, change => Assert.NotEqual(baselineKey, _builder.Build(change)));
        Assert.NotEqual(
            _builder.Build(baseline with { Target = orderedTarget }),
            _builder.Build(baseline with { Target = reorderedTarget }));
    }

    private static RuntimeMaterializationCacheKeyRequest Request(string root)
    {
        var assetRoot = Path.Combine(root, "runtime");
        var output = Path.Combine(root, "obj", "runtime.wasm");
        var target = RuntimePackTestData.Target("wasm32");
        return new(
            new(
                "sdk-1",
                "compiler-1",
                "runtime-1",
                "runtime-pack-1",
                "host-tools",
                "host-tools-1",
                "wasm-ld-1",
                "wasm-opt-1",
                "wasm-tools-1",
                "node-1"),
            RuntimePackTestData.Manifest(),
            target,
            RuntimePackTestData.Layout(),
            RuntimeWasmOptimization.Oz,
            ImmutableArray.Create(
                "-mwasm32",
                Path.Combine(assetRoot, target.RuntimeArchive.Path),
                $"--allow-undefined-file={Path.Combine(assetRoot, target.AllowedUndefinedSymbols.Path)}",
                "-o",
                output),
            ImmutableArray.Create("-Oz", output, "-o", output),
            assetRoot,
            output);
    }
}
