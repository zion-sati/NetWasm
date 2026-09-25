using System.Reflection;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class EmbeddedCorpusCaseAssetsReaderTests
{
    [Fact]
    public void ReadsOnlyDeclaredCorpusResourcesInStableOrderAndDisposesStreams()
    {
        var assembly = new ResourceAssembly(new()
        {
            ["Other/asset"] = "not a case",
            ["Correctness/cil-coverage-inventory.json"] = """{"semanticFamilies":[{"id":"S06"},{"id":"S01"}]}""",
            ["Correctness/Fixtures/z.cs"] = "secondary source",
            ["Correctness/Fixtures/b.case.json"] = "second manifest\r\n",
            ["Correctness/Fixtures/a.case.json"] = "first manifest\n",
            ["Correctness/Fixtures/a.cs.txt"] = "primary source",
        });
        var reader = Assert.IsAssignableFrom<ICorpusCaseAssetsReader>(new EmbeddedCorpusCaseAssetsReader(assembly));

        var assets = reader.Read();

        Assert.Equal(["S01", "S06"], assets.Features.Ids.Order(StringComparer.Ordinal));
        Assert.Equal<CorpusManifestAsset>([new("a.case.json", "first manifest\n"), new("b.case.json", "second manifest\r\n")], assets.Manifests);
        Assert.Equal<string>(["a.cs.txt", "z.cs"], assets.SourceFiles);
        Assert.Equal(3, assembly.Streams.Count);
        Assert.All(assembly.Streams, stream => Assert.True(stream.Disposed));
    }

    [Fact]
    public void MissingInventoryDoesNotCreateASecondFeatureCatalog()
    {
        var assembly = new ResourceAssembly([]);
        var reader = Assert.IsAssignableFrom<ICorpusCaseAssetsReader>(new EmbeddedCorpusCaseAssetsReader(assembly));

        Assert.Throws<InvalidOperationException>(() => reader.Read());
        Assert.Empty(assembly.Streams);
    }

    [Fact]
    public void UnknownAssetFailsLoudlyAndDisposesTheInventory()
    {
        var assembly = new ResourceAssembly(new()
        {
            ["Correctness/cil-coverage-inventory.json"] = """{"semanticFamilies":[]}""",
            ["Correctness/Fixtures/unknown.bin"] = "not an accepted asset",
        });
        var reader = Assert.IsAssignableFrom<ICorpusCaseAssetsReader>(new EmbeddedCorpusCaseAssetsReader(assembly));

        Assert.Throws<InvalidOperationException>(() => reader.Read());
        Assert.True(Assert.Single(assembly.Streams).Disposed);
    }

    [Fact]
    public void DuplicateFeatureIdentityFailsRatherThanBeingSilentlyCollapsed()
    {
        var assembly = new ResourceAssembly(new()
        {
            ["Correctness/cil-coverage-inventory.json"] = """{"semanticFamilies":[{"id":"S06"},{"id":"S06"}]}""",
        });
        var reader = Assert.IsAssignableFrom<ICorpusCaseAssetsReader>(new EmbeddedCorpusCaseAssetsReader(assembly));

        Assert.Throws<ArgumentException>(() => reader.Read());
        Assert.True(Assert.Single(assembly.Streams).Disposed);
    }

    private sealed class ResourceAssembly(Dictionary<string, string> resources) : Assembly
    {
        public List<TrackedStream> Streams { get; } = [];

        public override string[] GetManifestResourceNames() => [.. resources.Keys];

        public override Stream? GetManifestResourceStream(string name)
        {
            if (!resources.TryGetValue(name, out var value))
            {
                return null;
            }
            var stream = new TrackedStream(Encoding.UTF8.GetBytes(value));
            Streams.Add(stream);
            return stream;
        }
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
