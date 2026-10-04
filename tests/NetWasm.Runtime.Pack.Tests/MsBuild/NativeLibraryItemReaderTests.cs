using System.Collections;
using Microsoft.Build.Framework;
using NetWasm.Runtime.Pack.MsBuild;

namespace NetWasm.Runtime.Pack.Tests.MsBuild;

public sealed class NativeLibraryItemReaderTests
{
    [Fact]
    public void ReadsEvaluatedProviderPathsWithoutOpeningArchives()
    {
        var reader = Assert.IsAssignableFrom<INativeLibraryItemReader>(new NativeLibraryItemReader());
        var item = Item("mule", "wasm64", "/package/native/../native/wasm64/libmule.a");

        var descriptor = Assert.Single(reader.Read([item]));

        Assert.Equal("mule", descriptor.LibraryName);
        Assert.Equal("wasm64", descriptor.Target);
        Assert.Equal("/package/native/wasm64/libmule.a", descriptor.Path);
        Assert.Empty(reader.Read([]));
    }

    [Theory]
    [InlineData("", "wasm32", "/native/libmule.a")]
    [InlineData(" ", "wasm32", "/native/libmule.a")]
    [InlineData("mule\nother", "wasm32", "/native/libmule.a")]
    [InlineData("mule", "", "/native/libmule.a")]
    [InlineData("mule", "Wasm32", "/native/libmule.a")]
    [InlineData("mule", "wasm32", "")]
    [InlineData("mule", "wasm32", "relative/libmule.a")]
    [InlineData("mule", "wasm32", "/native/lib\nmule.a")]
    public void RejectsMissingOrAmbiguousMetadata(string library, string target, string path)
    {
        var reader = Assert.IsAssignableFrom<INativeLibraryItemReader>(new NativeLibraryItemReader());

        var error = Assert.Throws<InvalidOperationException>(() => reader.Read([Item(library, target, path)]));

        Assert.Contains("requires NetWasmLibraryName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingItemsAndArchiveIdentity()
    {
        var reader = Assert.IsAssignableFrom<INativeLibraryItemReader>(new NativeLibraryItemReader());
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Throws<InvalidOperationException>(() => reader.Read([null!]));
        var item = Item("mule", "wasm32", "/native/libmule.a");
        item.ItemSpec = " ";
        Assert.Throws<InvalidOperationException>(() => reader.Read([item]));
    }

    private static ItemStub Item(string library, string target, string path) => new()
    {
        Values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NetWasmLibraryName"] = library,
            ["WasmTarget"] = target,
            ["FullPath"] = path,
        },
    };

    private sealed class ItemStub : ITaskItem
    {
        public string ItemSpec { get; set; } = "native/libmule.a";
        public Dictionary<string, string> Values { get; init; } = [];
        public int MetadataCount => Values.Count;
        public ICollection MetadataNames => Values.Keys;
        public string GetMetadata(string metadataName) => Values.GetValueOrDefault(metadataName, string.Empty);
        public void SetMetadata(string metadataName, string metadataValue) => Values[metadataName] = metadataValue;
        public void RemoveMetadata(string metadataName) => Values.Remove(metadataName);
        public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(Values);
        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            foreach (var (name, value) in Values)
                destinationItem.SetMetadata(name, value);
        }
    }
}
