using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimePackTargetSelectorTests
{
    public static IEnumerable<object[]> ProfileCases()
    {
        foreach (var target in new[] { "wasm32", "wasm64" })
            foreach (var collector in new[] { "Compact", "NetWasm", "Bump" })
                foreach (var mode in Enum.GetValues<RuntimeWasmOptimization>())
                    yield return [target, collector, mode];
    }

    [Theory]
    [MemberData(nameof(ProfileCases))]
    public void SelectsProfilePairAndItsOwnFootprint(string target, string collector, RuntimeWasmOptimization mode)
    {
        var manifest = RuntimePackTestData.Manifest();
        var original = manifest.TargetLookup[(target, collector)];
        var speed = original.Speed! with { RuntimeFootprintBytes = original.RuntimeFootprintBytes + 16_384 };
        original = original with { Speed = speed };
        manifest = manifest with { TargetLookup = manifest.TargetLookup.SetItem((target, collector), original) };
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(
            new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()));

        var result = selector.Select(manifest, target, collector, mode);
        var preferSpeed = mode is RuntimeWasmOptimization.O1 or RuntimeWasmOptimization.O2 or RuntimeWasmOptimization.O3;
        Assert.Equal(preferSpeed ? speed.RuntimeArchive : original.RuntimeArchive, result.RuntimeArchive);
        Assert.Equal(preferSpeed ? speed.CollectorArchive : original.CollectorArchive, result.CollectorArchive);
        Assert.Equal(preferSpeed ? speed.RuntimeFootprintBytes : original.RuntimeFootprintBytes, result.RuntimeFootprintBytes);
        Assert.Equal(preferSpeed ? speed.PlatformArchives : original.PlatformArchives, result.PlatformArchives);
        Assert.Equal(original.Target, result.Target);
        Assert.Equal(original.GarbageCollector, result.GarbageCollector);
        Assert.Same(original.SystemLibraries, result.SystemLibraries);
        Assert.Same(original.NativeValidation, result.NativeValidation);
        Assert.Same(original.AllowedUndefinedSymbols, result.AllowedUndefinedSymbols);
        Assert.Equal(original.NativeStackSizeBytes, result.NativeStackSizeBytes);
        Assert.Same(original, manifest.TargetLookup[(target, collector)]);
    }

    [Fact]
    public void ResolvesNativeCapabilityInsteadOfTestingPresetLocally()
    {
        var policy = new FixedPolicy();
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector(policy));
        var manifest = RuntimePackTestData.Manifest();
        var result = selector.Select(manifest, "wasm64", null, RuntimeWasmOptimization.Oz);

        Assert.Equal(RuntimeWasmOptimization.Oz, policy.Mode);
        Assert.Equal(manifest.TargetLookup[("wasm64", "Compact")].Speed!.RuntimeArchive, result.RuntimeArchive);
    }

    [Fact]
    public void RejectsUnavailableSpeedAssetsWithoutSizeFallback()
    {
        var manifest = RuntimePackTestData.Manifest();
        var key = ("wasm32", "Compact");
        manifest = manifest with { TargetLookup = manifest.TargetLookup.SetItem(key, manifest.TargetLookup[key] with { Speed = null }) };
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()));

        Assert.Throws<InvalidOperationException>(() => selector.Select(manifest, key.Item1, key.Item2, RuntimeWasmOptimization.O3));
        Assert.Throws<ArgumentOutOfRangeException>(() => selector.Select(manifest, key.Item1, key.Item2, (RuntimeWasmOptimization)100));
        Assert.Throws<ArgumentNullException>(() => new RuntimePackTargetSelector(null!));
    }

    private sealed class FixedPolicy : IRuntimeOptimizationPolicyResolver
    {
        public RuntimeWasmOptimization? Mode { get; private set; }

        public RuntimeOptimizationPolicy Resolve(RuntimeWasmOptimization optimization)
        {
            Mode = optimization;
            return new("-Oz", true);
        }
    }

    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "NetWasm")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "NetWasm")]
    [InlineData("wasm32", "Bump")]
    [InlineData("wasm64", "Bump")]
    public void SelectsMatchedArchivePair(string target, string collector)
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()));
        var selected = selector.Select(Manifest(), target, collector);
        Assert.Equal(target, selected.Target);
        Assert.Equal(collector, selected.GarbageCollector);
        Assert.Equal($"{target}/{collector.ToLowerInvariant()}/libnetwasm-runtime.a", selected.RuntimeArchive.Path);
        Assert.Equal($"{target}/{collector.ToLowerInvariant()}/libgc.a", selected.CollectorArchive.Path);
    }

    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "NetWasm")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "NetWasm")]
    [InlineData("wasm32", "Bump")]
    [InlineData("wasm64", "Bump")]
    public void ResolvesOnlyTheManifestDefault(string target, string collector)
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()));
        var manifest = Manifest(collector);
        Assert.Same(selector.Select(manifest, target, collector), selector.Select(manifest, target, null));
        Assert.Equal(collector, selector.Select(manifest, target, null).GarbageCollector);
    }

    [Theory]
    [InlineData("")]
    [InlineData("compact")]
    [InlineData("Boehm")]
    [InlineData("tcms")]
    public void RejectsNoncanonicalChoice(string collector)
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()));
        Assert.Throws<InvalidOperationException>(() => selector.Select(Manifest(), "wasm32", collector));
    }

    [Fact]
    public void RejectsMissingPairAndInvalidInputs()
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector(new RuntimeOptimizationPolicyResolver()));
        var manifest = Manifest();
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, "wasm32", null));
        Assert.Throws<ArgumentException>(() => selector.Select(manifest, " ", null));
        Assert.Throws<InvalidOperationException>(() => selector.Select(manifest, "wasm128", null));
        Assert.Throws<InvalidOperationException>(() => selector.Select(manifest with { DefaultGarbageCollector = "" }, "wasm32", null));
        Assert.Throws<InvalidOperationException>(() => selector.Select(manifest with
        {
            TargetLookup = ImmutableDictionary<(string, string), RuntimePackTarget>.Empty,
        }, "wasm32", "Compact"));
    }

    [Fact]
    public void RejectsOldSchemaDuplicateMissingAndInvalidVariants()
    {
        var source = SourceManifest();
        Assert.Throws<InvalidOperationException>(() => Read(source with { SchemaVersion = 4 }));
        Assert.Throws<InvalidOperationException>(() => Read(source with { Targets = source.Targets.RemoveAt(0) }));
        Assert.Throws<InvalidOperationException>(() => Read(source with { Targets = source.Targets.SetItem(0, source.Targets[1]) }));
        Assert.Throws<InvalidOperationException>(() => Read(source with { DefaultGarbageCollector = "compact" }));
        Assert.Throws<InvalidOperationException>(() => Read(source with { Targets = source.Targets.SetItem(0, source.Targets[0] with { GarbageCollector = "tcms" }) }));
        Assert.Throws<InvalidOperationException>(() => Read(source with { Targets = source.Targets.SetItem(0, source.Targets[0] with { CollectorArchive = RuntimePackTestData.Asset("wasm32/bump/libgc.a") }) }));
    }

    private static RuntimePackManifest Manifest(string collector = "Compact") => Read(SourceManifest() with
    {
        DefaultGarbageCollector = collector,
    });

    private static RuntimePackManifest Read(RuntimePackManifest manifest) =>
        new RuntimePackManifestParser().Parse(JsonSerializer.Serialize(manifest));

    private static RuntimePackManifest SourceManifest() => RuntimePackTestData.Manifest() with
    {
        SchemaVersion = 6,
        DefaultGarbageCollector = "Compact",
        Targets = [Variant("wasm32", "Compact"), Variant("wasm32", "NetWasm"), Variant("wasm32", "Bump"),
            Variant("wasm64", "Compact"), Variant("wasm64", "NetWasm"), Variant("wasm64", "Bump")],
    };

    private static RuntimePackTarget Variant(string target, string collector) => RuntimePackTestData.Target(target, collector);
}
