using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// Each platform's read-ahead call, made through stand-in native functions.
public sealed unsafe class UnitMemoryPrefetchTests
{
    // What the stand-ins were asked, and what they answer: madvise's 0 or -1, and
    // PrefetchVirtualMemory's nonzero or 0.
    [ThreadStatic] private static (nint Address, nuint Length, int Advice) s_advised;
    [ThreadStatic] private static (nint Process, nuint Count, nint Address, nuint Size, uint Flags) s_prefetched;
    [ThreadStatic] private static bool s_refuse;

    [UnmanagedCallersOnly]
    private static int Advise(void* address, nuint length, int advice)
    {
        s_advised = ((nint)address, length, advice);
        return s_refuse ? -1 : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PrefetchVirtualMemory(nint process, nuint count, VirtualMemoryUnitMemoryPrefetcher.MemoryRange* ranges,
        uint flags)
    {
        s_prefetched = (process, count, (nint)ranges->Address, ranges->Size, flags);
        return s_refuse ? 0 : 1;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdvisingAsksForTheRangeToBeReadAhead(bool refuse)
    {
        var buffer = stackalloc byte[64];
        s_refuse = refuse;

        var taken = new AdvisingUnitMemoryPrefetcher((nint)(delegate* unmanaged<void*, nuint, int, int>)&Advise).Prefetch(buffer, 64);

        Assert.Equal(((nint)buffer, (nuint)64, 3), s_advised);
        Assert.Equal(!refuse, taken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrefetchingVirtualMemoryAsksForTheCurrentProcessesRange(bool refuse)
    {
        var buffer = stackalloc byte[64];
        s_refuse = refuse;

        var taken = new VirtualMemoryUnitMemoryPrefetcher(
            (nint)(delegate* unmanaged[Stdcall]<nint, nuint, VirtualMemoryUnitMemoryPrefetcher.MemoryRange*, uint, int>)
            &PrefetchVirtualMemory).Prefetch(buffer, 64);

        Assert.Equal(((nint)(-1), (nuint)1, (nint)buffer, (nuint)64, 0u), s_prefetched);
        Assert.Equal(!refuse, taken);
    }

    // It never touches the range: an address nothing maps reads on demand.
    [Fact]
    public void WithoutAnOsCallNothingIsAsked() => Assert.False(new NoUnitMemoryPrefetcher().Prefetch(null, 4096));

    public static TheoryData<string, Type> Platforms => new()
    {
        { "LINUX", typeof(AdvisingUnitMemoryPrefetcher) },
        { "OSX", typeof(AdvisingUnitMemoryPrefetcher) },
        { "WINDOWS", typeof(VirtualMemoryUnitMemoryPrefetcher) },
    };

    [Theory]
    [MemberData(nameof(Platforms))]
    public void EachPlatformPrefetchesThroughItsOwnCall(string platform, Type prefetcher)
    {
        var exports = new FixedExports(42);

        var created = new UnitMemoryPrefetcherFactory(exports).Create(OSPlatform.Create(platform));

        Assert.IsType(prefetcher, created);
        Assert.Same(UnitMemoryPrefetcherFactory.Platforms[OSPlatform.Create(platform)].Export, Assert.Single(exports.Asked));
    }

    [Fact]
    public void APlatformWithoutTheCallReadsOnDemand()
    {
        var unknown = new FixedExports(42);
        var unresolved = new FixedExports(0);

        Assert.IsType<NoUnitMemoryPrefetcher>(new UnitMemoryPrefetcherFactory(unknown).Create(OSPlatform.FreeBSD));
        Assert.Empty(unknown.Asked);
        Assert.IsType<NoUnitMemoryPrefetcher>(new UnitMemoryPrefetcherFactory(unresolved).Create(OSPlatform.Linux));
    }

    [Fact]
    [Trait("Category", "HostInterop")]
    public void TheHostIsThePlatformThisProcessRunsOn() =>
        Assert.True(RuntimeInformation.IsOSPlatform(UnitMemoryPrefetcherFactory.Host()));

    // The compiler's composition prefetches through the host's own call, which takes a
    // mapped segment's request.
    [Fact]
    [Trait("Category", "HostInterop")]
    public void TheCompilerPrefetchesThroughTheHostsCall()
    {
        using var services = new ServiceCollection().AddNetWasmEmission().BuildServiceProvider();
        var prefetcher = services.GetRequiredService<IUnitMemoryPrefetcher>();
        var path = Path.Combine(Path.GetTempPath(), "netwasm-segment-" + Guid.NewGuid().ToString("N") + ".seg");
        File.WriteAllBytes(path, new byte[3 * Environment.SystemPageSize]);
        var answers = new AnsweringPrefetcher(prefetcher);
        try
        {
            using (new MappedUnitSegmentMemory(path, answers))
            {
            }
        }
        finally
        {
            File.Delete(path);
        }

        Assert.IsType(UnitMemoryPrefetcherFactory.Platforms[UnitMemoryPrefetcherFactory.Host()].Create(1).GetType(), prefetcher);
        Assert.Equal([true], answers.Answers);
    }

    private sealed class AnsweringPrefetcher(IUnitMemoryPrefetcher inner) : IUnitMemoryPrefetcher
    {
        public List<bool> Answers { get; } = [];

        public bool Prefetch(byte* address, nuint length)
        {
            var taken = inner.Prefetch(address, length);
            Answers.Add(taken);
            return taken;
        }
    }

    [Fact]
    public void EachStoreActorNeedsItsCollaborator()
    {
        Assert.Throws<ArgumentNullException>(() => new UnitMemoryPrefetcherFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new MappedFileUnitStoreLoader(null!));
        Assert.Throws<ArgumentNullException>(() => new FileUnitStoreCommitter(null!));
        Assert.Throws<ArgumentNullException>(() => new NativeLibraryExportResolver().Resolve(null!));
    }

    private sealed class FixedExports(nint function) : INativeExportResolver
    {
        public List<NativeExport> Asked { get; } = [];

        public nint Resolve(NativeExport export)
        {
            Asked.Add(export);
            return function;
        }
    }
}

// A native function is found in the first of its libraries that loads and has it.
[Trait("Category", "HostInterop")]
public sealed class NativeLibraryExportResolverTests
{
    private static NativeExport HostCall => UnitMemoryPrefetcherFactory.Platforms[UnitMemoryPrefetcherFactory.Host()].Export;

    [Fact]
    public void TheHostsCallIsFoundPastALibraryThatDoesNotLoad() =>
        Assert.NotEqual(0, new NativeLibraryExportResolver().Resolve(
            HostCall with { Libraries = ["netwasm-no-such-library", .. HostCall.Libraries] }));

    [Fact]
    public void AnExportNoLibraryHasIsNotFound() =>
        Assert.Equal(0, new NativeLibraryExportResolver().Resolve(HostCall with { Name = "netwasm_no_such_export" }));
}

// A mapped segment reads the file in place and asks for it to be read ahead; one that fails
// to map holds nothing of the file.
public sealed unsafe class MappedUnitSegmentMemoryTests
{
    [Fact]
    public void ASegmentIsReadInPlaceAndPrefetched()
    {
        var path = Segment([1, 2, 3, 4, 5]);
        var prefetcher = new RecordingPrefetcher();
        try
        {
            using (var memory = new MappedUnitSegmentMemory(path, prefetcher))
            {
                Assert.Equal(5, memory.Length);
                Assert.Equal([1, 2, 3, 4, 5], new ReadOnlySpan<byte>(memory.Pointer, 5).ToArray());
                var (address, length) = Assert.Single(prefetcher.Ranges);
                Assert.True((nint)memory.Pointer - address + 5 <= (nint)length, "the prefetch misses the segment's end");
            }
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASegmentThatFailsToMapReleasesItsFile()
    {
        var path = Segment([1, 2, 3]);
        try
        {
            var failure = Assert.Throws<InvalidOperationException>(() => new MappedUnitSegmentMemory(path, new FailingPrefetcher()));

            Assert.Equal("prefetch", failure.Message);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Segment(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-segment-" + Guid.NewGuid().ToString("N") + ".seg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class RecordingPrefetcher : IUnitMemoryPrefetcher
    {
        public List<(nint Address, nuint Length)> Ranges { get; } = [];

        public bool Prefetch(byte* address, nuint length)
        {
            Ranges.Add(((nint)address, length));
            return true;
        }
    }

    private sealed class FailingPrefetcher : IUnitMemoryPrefetcher
    {
        public bool Prefetch(byte* address, nuint length) => throw new InvalidOperationException("prefetch");
    }
}

// Committing deletes the segments the manifest no longer lists; one another process holds
// stays for the next compile, and the summary says so.
public sealed class FileUnitStoreCommitterTests
{
    [Fact]
    public void ASegmentAnotherProcessHoldsIsKeptAndNamed()
    {
        var location = Location();
        try
        {
            var directory = UnitStoreFiles.Directory(location);
            Directory.CreateDirectory(directory);
            foreach (var name in new[] { "units-1.seg", "units-2.seg", "units-x.seg" })
                File.WriteAllBytes(Path.Combine(directory, name), [1]);
            var files = new HoldingDeleter(new()
            {
                ["units-1.seg"] = new IOException("in use"),
                ["units.bin"] = new UnauthorizedAccessException("denied"),
            });

            new FileUnitStoreCommitter(files).Commit(location, Change());

            Assert.Equal(["units-1.seg", "units-x.seg", "units.bin"], files.Asked.Order(StringComparer.Ordinal));
            Assert.Equal("summary\nkept units-1.seg: another process holds it\nkept units.bin: another process holds it\n",
                File.ReadAllText(Path.Combine(directory, "last-compile.txt")));
        }
        finally
        {
            if (Directory.Exists(location)) Directory.Delete(location, recursive: true);
        }
    }

    // Any other failure fails the commit, before its summary is written.
    [Fact]
    public void AnyOtherDeleteFailureFailsTheCommit()
    {
        var location = Location();
        try
        {
            var files = new HoldingDeleter(new() { ["units.bin"] = new InvalidOperationException("broken") });

            Assert.Same(files.Failures["units.bin"],
                Assert.Throws<InvalidOperationException>(() => new FileUnitStoreCommitter(files).Commit(location, Change())));
            Assert.False(File.Exists(Path.Combine(UnitStoreFiles.Directory(location), "last-compile.txt")));
        }
        finally
        {
            if (Directory.Exists(location)) Directory.Delete(location, recursive: true);
        }
    }

    // Windows refuses to delete a file another handle opened without sharing deletion;
    // other hosts delete it.
    [Fact]
    [Trait("Category", "HostInterop")]
    public void ASegmentHeldOnTheHostIsKeptOnlyWhereTheHostRefusesItsDeletion()
    {
        var location = Location();
        try
        {
            var directory = UnitStoreFiles.Directory(location);
            Directory.CreateDirectory(directory);
            var held = Path.Combine(directory, "units-1.seg");
            File.WriteAllBytes(held, [1]);
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
                new FileUnitStoreCommitter(new UnitFileDeleter()).Commit(location, Change());

            Assert.Equal(OperatingSystem.IsWindows(), File.Exists(held));
            Assert.Equal(OperatingSystem.IsWindows(), File.ReadAllText(Path.Combine(directory, "last-compile.txt"))
                .Contains("kept units-1.seg: another process holds it", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(location)) Directory.Delete(location, recursive: true);
        }
    }

    [Fact]
    public void TheFileDeleterDeletesAFileAndToleratesNone()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-delete-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, [1]);

        new UnitFileDeleter().Delete(path);
        new UnitFileDeleter().Delete(path);

        Assert.False(File.Exists(path));
    }

    private static string Location() => Path.Combine(Path.GetTempPath(), "netwasm-commit-" + Guid.NewGuid().ToString("N"));

    // The manifest keeps sequence 2 alone.
    private static UnitStoreChange Change() => new(3, null, [1, 2, 3], new HashSet<int> { 2 }, "summary\n");

    private sealed class HoldingDeleter(Dictionary<string, Exception> failures) : IUnitFileDeleter
    {
        public Dictionary<string, Exception> Failures => failures;

        public List<string> Asked { get; } = [];

        public void Delete(string path)
        {
            var name = Path.GetFileName(path);
            Asked.Add(name);
            if (failures.TryGetValue(name, out var failure)) throw failure;
        }
    }
}
