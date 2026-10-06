using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimePackTargetSelectorTests
{
    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "Boehm")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "Boehm")]
    public void SelectsMatchedArchivePair(string target, string collector)
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector());
        var selected = selector.Select(Manifest(), target, collector);
        Assert.Equal(target, selected.Target);
        Assert.Equal(collector, selected.GarbageCollector);
        Assert.Equal($"{target}/{collector.ToLowerInvariant()}/libnetwasm-runtime.a", selected.RuntimeArchive.Path);
        Assert.Equal($"{target}/{collector.ToLowerInvariant()}/libgc.a", selected.CollectorArchive.Path);
    }

    [Theory]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm32", "Boehm")]
    [InlineData("wasm64", "Compact")]
    [InlineData("wasm64", "Boehm")]
    public void ResolvesOnlyTheManifestDefault(string target, string collector)
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector());
        var manifest = Manifest(collector);
        Assert.Same(selector.Select(manifest, target, collector), selector.Select(manifest, target, null));
        Assert.Equal(collector, selector.Select(manifest, target, null).GarbageCollector);
    }

    [Theory]
    [InlineData("")]
    [InlineData("compact")]
    [InlineData("Boehm ")]
    [InlineData("tcms")]
    public void RejectsNoncanonicalChoice(string collector)
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector());
        Assert.Throws<InvalidOperationException>(() => selector.Select(Manifest(), "wasm32", collector));
    }

    [Fact]
    public void RejectsMissingPairAndInvalidInputs()
    {
        var selector = Assert.IsAssignableFrom<IRuntimePackTargetSelector>(new RuntimePackTargetSelector());
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
        Assert.Throws<InvalidOperationException>(() => Read(source with { Targets = source.Targets.SetItem(0, source.Targets[0] with { CollectorArchive = RuntimePackTestData.Asset("wasm32/boehm/libgc.a") }) }));
    }

    private static RuntimePackManifest Manifest(string collector = "Boehm") => Read(SourceManifest() with
    {
        DefaultGarbageCollector = collector,
    });

    private static RuntimePackManifest Read(RuntimePackManifest manifest) =>
        RuntimePackManifestReader.ReadJson(JsonSerializer.Serialize(manifest));

    private static RuntimePackManifest SourceManifest() => RuntimePackTestData.Manifest() with
    {
        SchemaVersion = 5,
        DefaultGarbageCollector = "Boehm",
        Targets = [Variant("wasm32", "Compact"), Variant("wasm32", "Boehm"),
            Variant("wasm64", "Compact"), Variant("wasm64", "Boehm")],
    };

    private static RuntimePackTarget Variant(string target, string collector) => RuntimePackTestData.Target(target) with
    {
        GarbageCollector = collector,
        RuntimeArchive = RuntimePackTestData.Asset($"{target}/{collector.ToLowerInvariant()}/libnetwasm-runtime.a"),
        CollectorArchive = RuntimePackTestData.Asset($"{target}/{collector.ToLowerInvariant()}/libgc.a"),
    };
}
