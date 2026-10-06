using System.Text.Json;
using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Planning;

public sealed class RuntimeLinkPlannerTests
{
    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "Boehm")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "Boehm")]
    public void SelectsMatchedPairAndTracksResolvedDefault(string target, string collector)
    {
        var manifest = RuntimePackTestData.Manifest() with { DefaultGarbageCollector = collector };
        var request = new RuntimeLinkPlanRequest(JsonSerializer.Serialize(manifest), target, 948,
            SystemLibraries: SystemLibraries(RuntimePackTestData.Target(target)));
        var defaultPlan = RuntimeLinkPlanner.Plan(request);
        var explicitPlan = RuntimeLinkPlanner.Plan(request with { GarbageCollector = collector });
        var other = collector == "Compact" ? "Boehm" : "Compact";
        var otherPlan = RuntimeLinkPlanner.Plan(request with { GarbageCollector = other });
        Assert.Equal(collector, defaultPlan.GarbageCollector);
        Assert.Equal(defaultPlan.Cache, explicitPlan.Cache);
        Assert.Equal($"/runtime/{target}/{collector.ToLowerInvariant()}/libnetwasm-runtime.a", defaultPlan.Inputs[0].Path);
        Assert.Equal($"/runtime/{target}/{collector.ToLowerInvariant()}/libgc.a", defaultPlan.Inputs[1].Path);
        Assert.Equal(other, otherPlan.GarbageCollector);
        Assert.NotEqual(defaultPlan.Cache.Key, otherPlan.Cache.Key);
        Assert.DoesNotContain(defaultPlan.Inputs[0].Path, otherPlan.Arguments);
        Assert.DoesNotContain(defaultPlan.Inputs[1].Path, otherPlan.Arguments);
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void PreservesDesktopPolicyWithVirtualAssetPaths(string targetName)
    {
        var manifest = RuntimePackTestData.Manifest();
        var target = manifest.Targets.Single(item => item.Target == targetName && item.GarbageCollector == "Boehm");
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
        Assert.Equal(layout.RuntimeGlobalBase >= 1024,
            actual.OptimizationArguments.Contains("--low-memory-unused"));
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

    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "Boehm")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "Boehm")]
    public void SelectsOptionalRuntimeExportsFromCompilerFeatureEvidence(
        string targetName, string collector)
    {
        var manifestModel = RuntimePackTestData.Manifest() with
        {
            Exports =
            [
                .. RuntimePackTestData.Manifest().Exports,
                "ephemeron_handle_get_key",
                "ephemeron_handle_get_value",
                "ephemeron_handle_new",
                "ephemeron_handle_release",
            ],
        };
        var manifest = JsonSerializer.Serialize(manifestModel);
        var target = manifestModel.Targets.Single(item =>
            item.Target == targetName && item.GarbageCollector == collector);
        var request = new RuntimeLinkPlanRequest(
            manifest, targetName, 948, SystemLibraries: SystemLibraries(target))
        {
            GarbageCollector = collector,
        };

        var legacy = RuntimeLinkPlanner.Plan(request);
        var baseRuntime = RuntimeLinkPlanner.Plan(request with { RuntimeFeatures = [] });
        var ephemerons = RuntimeLinkPlanner.Plan(request with
        {
            RuntimeFeatures = ["ephemeron-handles"],
        });
        var diagnostics = RuntimeLinkPlanner.Plan(request with
        {
            RuntimeFeatures = ["structured-command-diagnostics"],
        });

        Assert.All([legacy, baseRuntime, ephemerons, diagnostics], plan =>
        {
            Assert.Equal(collector, plan.GarbageCollector);
            Assert.Equal($"/runtime/{targetName}/{collector.ToLowerInvariant()}/libnetwasm-runtime.a",
                plan.Inputs[0].Path);
            Assert.Equal($"/runtime/{targetName}/{collector.ToLowerInvariant()}/libgc.a",
                plan.Inputs[1].Path);
        });
        Assert.Contains("--export=ephemeron_handle_new", legacy.Arguments);
        Assert.Contains("--export=command_exception_capture", legacy.Arguments);
        Assert.DoesNotContain(baseRuntime.Arguments,
            argument => argument.Contains("ephemeron_handle", StringComparison.Ordinal));
        Assert.DoesNotContain(baseRuntime.Arguments,
            argument => argument.Contains("command_exception", StringComparison.Ordinal));
        Assert.Contains("--export=ephemeron_handle_new", ephemerons.Arguments);
        Assert.DoesNotContain(ephemerons.Arguments,
            argument => argument.Contains("command_exception", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.Arguments,
            argument => argument.Contains("ephemeron_handle", StringComparison.Ordinal));
        Assert.Contains("--export=command_exception_capture", diagnostics.Arguments);
        Assert.NotEqual(legacy.Cache.Key, baseRuntime.Cache.Key);
        Assert.NotEqual(baseRuntime.Cache.Key, ephemerons.Cache.Key);
        Assert.NotEqual(baseRuntime.Cache.Key, diagnostics.Cache.Key);
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

    [Fact]
    public void SelectsOnlyReachedNativeProvidersAndIncludesTheirIdentityInThePlan()
    {
        var manifest = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        var target = RuntimePackTestData.Target("wasm32");
        var request = new RuntimeLinkPlanRequest(manifest, "wasm32", 948,
            SystemLibraries: SystemLibraries(target))
        {
            NativeImports =
            [
                new("lz4", "LZ4_compressBound", [RuntimeLinkPlanNativeValueType.I32],
                    RuntimeLinkPlanNativeValueType.I32),
            ],
            NativeLibraries =
            [
                new("unused", "wasm32", "/native/libunused.a", new string('b', 64)),
                new("lz4", "wasm32", "/native/liblz4.a", new string('a', 64)),
            ],
        };

        var actual = RuntimeLinkPlanner.Plan(request);
        var withoutNative = RuntimeLinkPlanner.Plan(request with
        {
            NativeImports = [],
        });

        Assert.Contains("/native/liblz4.a", actual.Arguments);
        Assert.Contains("--undefined=LZ4_compressBound", actual.Arguments);
        Assert.Contains("--export=LZ4_compressBound", actual.Arguments);
        Assert.Contains(actual.Inputs, input => input == new RuntimeLinkPlanAsset(
            "/native/liblz4.a", new string('a', 64)));
        Assert.Contains(new RuntimeLinkPlanExport("LZ4_compressBound", 0), actual.InternalRuntimeExports);
        Assert.Contains(new RuntimeLinkPlanExport("__heap_base", 3), actual.InternalRuntimeExports);
        Assert.DoesNotContain(actual.Arguments, argument => argument.Contains("libunused", StringComparison.Ordinal));
        Assert.DoesNotContain(actual.Inputs, input => input.Path.Contains("libunused", StringComparison.Ordinal));
        Assert.NotEqual(withoutNative.Cache.Key, actual.Cache.Key);
        Assert.Empty(withoutNative.InternalRuntimeExports);
    }

    [Fact]
    public void RejectsMissingConflictingAndUnsafeNativeProviders()
    {
        var manifest = JsonSerializer.Serialize(RuntimePackTestData.Manifest());
        var target = RuntimePackTestData.Target("wasm32");
        var request = new RuntimeLinkPlanRequest(manifest, "wasm32", 948,
            SystemLibraries: SystemLibraries(target))
        {
            NativeImports = [new("sample", "native_add", [], RuntimeLinkPlanNativeValueType.I32)],
        };

        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(request));
        Assert.Throws<ArgumentException>(() => RuntimeLinkPlanner.Plan(request with
        {
            NativeLibraries = [new("sample", "wasm32", "relative/libsample.a", new string('a', 64))],
        }));
        Assert.Throws<InvalidOperationException>(() => RuntimeLinkPlanner.Plan(request with
        {
            NativeLibraries =
            [
                new("sample", "wasm32", "/native/one.a", new string('a', 64)),
                new("sample", "wasm32", "/native/two.a", new string('b', 64)),
            ],
        }));
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
            OutputPath: "/runtime/wasm32/boehm/libnetwasm-runtime.a",
            SystemLibraries: SystemLibraries(RuntimePackTestData.Target("wasm32")))));
    }

    private static ImmutableArray<RuntimeLinkPlanAsset> SystemLibraries(RuntimePackTarget target) =>
        target.SystemLibraries.Names
            .Select(name => new RuntimeLinkPlanAsset($"/emscripten/{name}", RuntimePackTestData.Digest))
            .ToImmutableArray();

    private static bool IsOptimizationFlag(string argument) =>
        argument is "-O0" or "-O1" or "-O2" or "-O3" or "-Os" or "-Oz";
}
