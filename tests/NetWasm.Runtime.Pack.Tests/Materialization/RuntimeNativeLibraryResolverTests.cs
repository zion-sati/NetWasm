using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeLibraryResolverTests
{
    [Fact]
    public void ReadsEachReachedCanonicalArchiveOnceAndPreservesDeclaredMappings()
    {
        var archives = new RecordingArchiveReader();
        var resolver = Assert.IsAssignableFrom<IRuntimeNativeLibraryResolver>(new RuntimeNativeLibraryResolver(archives));

        var result = resolver.Resolve(new("wasm32", [Import("mule"), Import("__Internal")],
        [new("mule", "wasm32", "/native/libmule.a"),
         new("__Internal", "wasm32", "/native/../native/libmule.a"),
         new("mule", "wasm32", "/native/libmule.a")]));

        Assert.Equal(["/native/libmule.a"], archives.Paths.ToArray());
        Assert.Equal(3, result.Length);
        Assert.Equal(["mule", "__Internal", "mule"], result.Select(item => item.LibraryName).ToArray());
        Assert.All(result, item =>
        {
            Assert.Equal("wasm32", item.Target);
            Assert.Equal("/native/libmule.a", item.Path);
            Assert.Equal(RuntimePackTestData.Digest, item.Sha256);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotReadUnusedOrOtherTargetArchives(bool hasReachedImport)
    {
        var archives = new RecordingArchiveReader(new InvalidOperationException("unexpected read"));
        var resolver = Assert.IsAssignableFrom<IRuntimeNativeLibraryResolver>(new RuntimeNativeLibraryResolver(archives));

        var result = resolver.Resolve(new("wasm64", hasReachedImport ? [Import("mule")] : [],
            [new("mule", "wasm32", "/missing/libmule.a"), new("unused", "wasm64", "/missing/libunused.a")]));

        Assert.Empty(result);
        Assert.Empty(archives.Paths);
    }

    [Fact]
    public void ResolvesCurrentBytesOnEachInvocation()
    {
        var archives = new RecordingArchiveReader();
        var resolver = Assert.IsAssignableFrom<IRuntimeNativeLibraryResolver>(new RuntimeNativeLibraryResolver(archives));
        var request = new RuntimeNativeLibraryResolutionRequest("wasm32", [Import("mule")], [new("mule", "wasm32", "/native/libmule.a")]);
        Assert.Equal(RuntimePackTestData.Digest, Assert.Single(resolver.Resolve(request)).Sha256);
        archives.Digest = new('b', 64);

        Assert.Equal(new string('b', 64), Assert.Single(resolver.Resolve(request)).Sha256);
        Assert.Equal(2, archives.Paths.Count);
    }

    [Theory]
    [InlineData("", "wasm32", "/native/libmule.a")]
    [InlineData("mule\0", "wasm32", "/native/libmule.a")]
    [InlineData("mule", "", "/native/libmule.a")]
    [InlineData("mule", "wasm128", "/native/libmule.a")]
    [InlineData("mule", "wasm32", "")]
    [InlineData("mule", "wasm32", "relative/libmule.a")]
    [InlineData("mule", "wasm32", "/native/libmule\r.a")]
    public void RejectsAllMalformedMetadataBeforeTheFirstRead(string library, string target, string path)
    {
        var archives = new RecordingArchiveReader();
        var resolver = Assert.IsAssignableFrom<IRuntimeNativeLibraryResolver>(new RuntimeNativeLibraryResolver(archives));
        var error = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new("wasm32", [Import("mule")],
            [new("mule", "wasm32", "/native/libmule.a"), new(library, target, path)])));
        Assert.Contains("metadata is invalid", error.Message, StringComparison.Ordinal);
        Assert.Empty(archives.Paths);
    }

    [Fact]
    public void ArchiveFailureStopsBeforeLaterReads()
    {
        var failure = new InvalidOperationException("archive failure");
        var archives = new RecordingArchiveReader(failure);
        var resolver = Assert.IsAssignableFrom<IRuntimeNativeLibraryResolver>(new RuntimeNativeLibraryResolver(archives));

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new("wasm32", [Import("mule")],
            [new("mule", "wasm32", "/native/first.a"), new("mule", "wasm32", "/native/second.a")]))));
        Assert.Equal(["/native/first.a"], archives.Paths.ToArray());
    }

    [Fact]
    public void RejectsMissingRequestsAndInvalidIdentitiesWithoutReading()
    {
        var archives = new RecordingArchiveReader();
        var resolver = Assert.IsAssignableFrom<IRuntimeNativeLibraryResolver>(new RuntimeNativeLibraryResolver(archives));
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeLibraryResolver(null!));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        foreach (var request in new RuntimeNativeLibraryResolutionRequest[]
        {
            new("", [], []), new("wasm32", default, []), new("wasm32", [], default),
            new("wasm32", [], [null!]), new("wasm32", [null!], []), new("wasm32", [Import("")], []),
        })
            Assert.Throws<InvalidOperationException>(() => resolver.Resolve(request));
        Assert.Empty(archives.Paths);
    }

    private static RuntimeNativeImport Import(string library) => new(library, "run", [], null);

    private sealed class RecordingArchiveReader(Exception? failure = null) : IRuntimeNativeArchiveReader
    {
        public List<string> Paths { get; } = [];
        public string Digest { get; set; } = RuntimePackTestData.Digest;
        public string Read(string path)
        {
            Paths.Add(path);
            if (failure is not null)
                throw failure;
            return Digest;
        }
    }
}
