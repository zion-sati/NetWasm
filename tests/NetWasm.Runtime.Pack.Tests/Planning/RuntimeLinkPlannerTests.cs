using System.Text.Json;
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
        var request = new RuntimeLinkPlanRequest(JsonSerializer.Serialize(manifest), targetName, 948);
        var layout = new RuntimeMemoryLayoutCalculator().Calculate(new(target, manifest.WasmPageSize, 948, null, null));
        var native = new RuntimeLinkArgumentBuilder().Build(new(manifest, target, layout, request.AssetRoot, request.OutputPath)).ToArray();

        var actual = RuntimeLinkPlanner.Plan(request);

        native[2] = actual.Inputs[0].Path;
        for (var index = 1; index < actual.Inputs.Length; index++) native[3 + index] = actual.Inputs[index].Path;
        native[^1] = request.OutputPath;
        Assert.Equal(native, actual.Arguments);
        Assert.Equal(layout.RuntimeGlobalBase, actual.RuntimeGlobalBase);
        Assert.Equal(layout.InitialMemorySizeBytes, actual.InitialMemorySizeBytes);
        Assert.Equal(layout.MaximumMemorySizeBytes, actual.MaximumMemorySizeBytes);
        Assert.Equal(manifest.RuntimeAbi, actual.RuntimeAbi);
        Assert.Equal(manifest.Provenance.ToolchainFingerprint, actual.ToolchainFingerprint);
        Assert.Equal(target.RuntimeArchive.Sha256, actual.Inputs[0].Sha256);
        Assert.All(actual.Inputs, asset => Assert.StartsWith("/runtime/", asset.Path));
    }

    [Fact]
    public void RecalculatesMemoryFromEachApplicationStaticEnd()
    {
        var manifest = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        var small = RuntimeLinkPlanner.Plan(new(manifest, "wasm32", 948));
        var large = RuntimeLinkPlanner.Plan(new(manifest, "wasm32", 200_000));
        Assert.True(large.RuntimeGlobalBase > small.RuntimeGlobalBase);
        Assert.True(large.InitialMemorySizeBytes > small.InitialMemorySizeBytes);
        Assert.NotEqual(small.Arguments, large.Arguments);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("/runtime/../outside")]
    [InlineData("/runtime/./asset")]
    public void RejectsUnsafeVirtualPaths(string path)
    {
        var json = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(new(json, "wasm32", 948, AssetRoot: path)));
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(new(json, "wasm32", 948, OutputPath: path)));
    }

    [Fact]
    public void RejectsMissingTargetAndOutputThatOverwritesInput()
    {
        var json = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(new(json, "unsupported", 948)));
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(new(json, "wasm32", 948,
            OutputPath: "/runtime/wasm32/libnetwasm-runtime.a")));
    }
}
