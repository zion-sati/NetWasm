using System.Text.Json;
using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Planning;

public sealed class RuntimeLinkPlannerTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void PreservesDesktopPolicyWithVirtualAssetPaths(string targetName)
    {
        var manifest = RuntimePackTestData.Manifest();
        var target = manifest.Targets.Single(item => item.Target == targetName);
        var systemLibraries = SystemLibraries(target);
        var request = new RuntimeLinkPlanRequest(
            JsonSerializer.Serialize(manifest), targetName, 948, SystemLibraries: systemLibraries);
        var layout = new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()).Calculate(new(target, manifest.WasmPageSize, 948, null, null));
        var native = new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(new(
            manifest, target, new RuntimeLinkMemoryLimits(layout.RuntimeGlobalBase, layout.InitialMemorySizeBytes,
                layout.MaximumMemorySizeBytes), request.AssetRoot,
            systemLibraries.Select(asset => asset.Path).ToImmutableArray(), request.OutputPath)).ToArray();

        var actual = RuntimeLinkPlanner.Plan(request);

        Assert.Equal(native.Length, actual.Arguments.Length);
        Assert.Contains(actual.Inputs[0].Path, actual.Arguments);
        Assert.Contains(actual.Inputs[1].Path, actual.Arguments);
        Assert.Contains(
            $"--allow-undefined-file={actual.Inputs[2].Path}",
            actual.Arguments);
        Assert.Equal(request.OutputPath, actual.Arguments[^1]);
        Assert.Equal(2,
            actual.OptimizationArguments.Count(argument => argument == request.OutputPath));
        Assert.Contains("--post-emscripten", actual.OptimizationArguments);
        Assert.Equal(layout.RuntimeGlobalBase, actual.RuntimeGlobalBase);
        Assert.Equal(layout.InitialMemorySizeBytes, actual.InitialMemorySizeBytes);
        Assert.Equal(layout.MaximumMemorySizeBytes, actual.MaximumMemorySizeBytes);
        Assert.Equal(manifest.RuntimeAbi, actual.RuntimeAbi);
        Assert.Equal(manifest.Provenance.ToolchainFingerprint, actual.ToolchainFingerprint);
        Assert.Equal(target.RuntimeArchive.Sha256, actual.Inputs[0].Sha256);
        Assert.Equal(target.CollectorArchive.Sha256, actual.Inputs[1].Sha256);
        Assert.Equal(target.AllowedUndefinedSymbols.Sha256, actual.Inputs[2].Sha256);
        Assert.All(actual.Inputs, asset => Assert.StartsWith("/", asset.Path));
        Assert.Equal("runtime-materialization-cache-v1", actual.Cache.Schema);
        Assert.All([actual.Cache.Namespace, actual.Cache.Slot, actual.Cache.Key], value =>
            Assert.Equal(64, value.Length));
    }

    [Fact]
    public void RecalculatesMemoryFromEachApplicationStaticEnd()
    {
        var manifest = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        var target = RuntimePackTestData.Target("wasm32");
        var libraries = SystemLibraries(target);
        var small = RuntimeLinkPlanner.Plan(new(manifest, "wasm32", 948, SystemLibraries: libraries));
        var large = RuntimeLinkPlanner.Plan(new(manifest, "wasm32", 200_000, SystemLibraries: libraries));
        Assert.True(large.RuntimeGlobalBase > small.RuntimeGlobalBase);
        Assert.True(large.InitialMemorySizeBytes > small.InitialMemorySizeBytes);
        Assert.NotEqual(small.Arguments, large.Arguments);
        Assert.Equal(small.Cache.Slot, large.Cache.Slot);
        Assert.NotEqual(small.Cache.Key, large.Cache.Key);
    }

    [Theory]
    [InlineData(RuntimeWasmOptimization.None, null)]
    [InlineData(RuntimeWasmOptimization.O0, "-O0")]
    [InlineData(RuntimeWasmOptimization.O1, "-O1")]
    [InlineData(RuntimeWasmOptimization.O2, "-O2")]
    [InlineData(RuntimeWasmOptimization.O3, "-O3")]
    [InlineData(RuntimeWasmOptimization.Os, "-Os")]
    [InlineData(RuntimeWasmOptimization.Oz, "-Oz")]
    public void PlansAuthoritativeOptimizationArguments(
        RuntimeWasmOptimization optimization,
        string? optimizationFlag)
    {
        var target = RuntimePackTestData.Target("wasm32");
        var plan = RuntimeLinkPlanner.Plan(new(
            JsonSerializer.Serialize(RuntimePackTestData.Manifest()),
            "wasm32",
            948,
            SystemLibraries: SystemLibraries(target),
            Optimization: optimization));

        if (optimizationFlag is null)
        {
            Assert.Empty(plan.OptimizationArguments);
            return;
        }

        Assert.Equal(optimizationFlag,
            Assert.Single(plan.OptimizationArguments, IsOptimizationFlag));
    }

    [Fact]
    public void RejectsUndefinedOptimization()
    {
        var target = RuntimePackTestData.Target("wasm32");
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeLinkPlanner.Plan(new(
            JsonSerializer.Serialize(RuntimePackTestData.Manifest()),
            "wasm32",
            948,
            SystemLibraries: SystemLibraries(target),
            Optimization: (RuntimeWasmOptimization)42)));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("/runtime/../outside")]
    [InlineData("/runtime/./asset")]
    [InlineData("/runtime\\asset")]
    [InlineData("/runtime/")]
    public void RejectsUnsafeVirtualPaths(string path)
    {
        var json = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        var libraries = SystemLibraries(RuntimePackTestData.Target("wasm32"));
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(new(json, "wasm32", 948, AssetRoot: path, SystemLibraries: libraries)));
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(new(json, "wasm32", 948, OutputPath: path, SystemLibraries: libraries)));
    }

    [Fact]
    public void RejectsIncompleteSystemLibraryClosure()
    {
        var json = JsonSerializer.Serialize(RuntimePackTestData.Manifest());

        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(new(
            json,
            "wasm32",
            948)));
        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(new(
            json,
            "wasm32",
            948,
            SystemLibraries: [])));
    }

    [Theory]
    [InlineData("/emscripten/not-libc.a", RuntimePackTestData.Digest)]
    [InlineData("/emscripten/libc.a", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void RejectsInvalidSystemLibraryClosure(string path, string digest)
    {
        var json = JsonSerializer.Serialize(RuntimePackTestData.Manifest());

        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(new(
            json,
            "wasm32",
            948,
            SystemLibraries: [new(path, digest)])));
    }

    [Fact]
    public void RejectsMissingTargetAndOutputThatOverwritesInput()
    {
        var json = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(new(json, "unsupported", 948)));
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(new(json, "wasm32", 948,
            OutputPath: "/runtime/wasm32/libnetwasm-runtime.a",
            SystemLibraries: SystemLibraries(RuntimePackTestData.Target("wasm32")))));
    }

    private static ImmutableArray<RuntimeLinkPlanAsset> SystemLibraries(RuntimePackTarget target) =>
        target.SystemLibraries.Names
            .Select(name => new RuntimeLinkPlanAsset($"/emscripten/{name}", RuntimePackTestData.Digest))
            .ToImmutableArray();

    private static bool IsOptimizationFlag(string argument) =>
        argument is "-O0" or "-O1" or "-O2" or "-O3" or "-Os" or "-Oz";
}
