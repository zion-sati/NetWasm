using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// One compile's change to its store: the segment it adds (if any), the manifest that
/// replaces the old one (null when the store is unchanged), the sequences the manifest
/// keeps alive, and a plain summary of the compile's reuse.
/// </summary>
internal readonly record struct UnitStoreChange(
    int Sequence,
    UnitSegmentWriter? Segment,
    byte[]? Manifest,
    IReadOnlySet<int> LiveSequences,
    string Summary);

/// <summary>
/// A compile as a later one can patch it: its module, each method's function index in it
/// (<see cref="UnitFunctionIndices"/>), its whole-program plans (<see cref="UnitPlanCodec"/>)
/// and the facts of its result a host writes beside the module.
/// </summary>
internal sealed record UnitKeptCompile(byte[] Module, byte[] FunctionIndices, byte[] Plans, byte[] Facts);

internal interface IUnitKeptCompileCommitter
{
    // Replaces the compile kept for a location.
    void Commit(string location, UnitKeptCompile kept);
}

internal interface IUnitStoreLoader
{
    // The store kept for a location; empty when there is none or it is of another version.
    UnitStore Load(string location);
}

internal interface IUnitKeptCompileLoader
{
    // The last compile kept for a location, or none.
    UnitKeptCompile? Load(string location);
}

internal interface IUnitStoreCommitter
{
    // Adds the change's segment, replaces the manifest and drops the segments it no longer lists.
    void Commit(string location, UnitStoreChange change);
}

/// <summary>Where a desktop compile keeps its units: beside its intermediate output.</summary>
internal static class UnitStoreFiles
{
    private const string SegmentPrefix = "units-";
    private const string SegmentExtension = ".seg";

    public static string Directory(string location) => Path.Combine(location, "netwasm-units");

    public static string Manifest(string directory) => Path.Combine(directory, "units.manifest");

    // The compile's inputs, kept beside its units.
    public static string InputsPath(string location) => Path.Combine(Directory(location), "inputs.bin");

    // The compile's module and each method's function index in it.
    public static string ModulePath(string location) => Path.Combine(Directory(location), "module.wasm");

    public static string FunctionIndicesPath(string location) => Path.Combine(Directory(location), "functions.bin");

    public static string PlansPath(string location) => Path.Combine(Directory(location), "plans.bin");

    public static string FactsPath(string location) => Path.Combine(Directory(location), "facts.json");

    public static string Segment(string directory, int sequence) =>
        Path.Combine(directory, SegmentPrefix + sequence.ToString(CultureInfo.InvariantCulture) + SegmentExtension);

    public static bool TryParseSequence(string path, out int sequence)
    {
        var name = Path.GetFileName(path.AsSpan());
        sequence = 0;
        return name.StartsWith(SegmentPrefix, StringComparison.Ordinal) &&
               name.EndsWith(SegmentExtension, StringComparison.Ordinal) &&
               int.TryParse(name[SegmentPrefix.Length..^SegmentExtension.Length], NumberStyles.None,
                   CultureInfo.InvariantCulture, out sequence);
    }
}

/// <summary>
/// Desktop loading: the manifest is read, and each segment is memory-mapped read-only, so
/// its pages come straight from the page cache the last compile filled and nothing lands on
/// the GC heap. The OS is asked to read each mapping ahead.
/// </summary>
internal sealed class MappedFileUnitStoreLoader(IUnitMemoryPrefetcher prefetcher) : IUnitStoreLoader
{
    private readonly IUnitMemoryPrefetcher _prefetcher = prefetcher ?? throw new ArgumentNullException(nameof(prefetcher));

    public UnitStore Load(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        var directory = UnitStoreFiles.Directory(location);
        var manifest = UnitStoreFiles.Manifest(directory);
        if (!UnitStoreFormat.IsSupported || !File.Exists(manifest))
            return UnitStore.Empty;
        return UnitStore.Over(File.ReadAllBytes(manifest),
            sequence => new MappedUnitSegmentMemory(UnitStoreFiles.Segment(directory, sequence), _prefetcher));
    }
}

/// <summary>
/// Desktop: the kept compile's files beside the units, each written beside its name and moved
/// into place; the facts last, so a reader that finds them finds the rest of this compile.
/// The facts are removed first, so a compile that fails midway leaves none.
/// </summary>
internal sealed class FileUnitKeptCompileCommitter : IUnitKeptCompileCommitter
{
    public void Commit(string location, UnitKeptCompile kept)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentNullException.ThrowIfNull(kept);
        System.IO.Directory.CreateDirectory(UnitStoreFiles.Directory(location));
        File.Delete(UnitStoreFiles.FactsPath(location));
        FileUnitStoreCommitter.WriteAside(UnitStoreFiles.ModulePath(location), stream => stream.Write(kept.Module));
        FileUnitStoreCommitter.WriteAside(UnitStoreFiles.FunctionIndicesPath(location), stream => stream.Write(kept.FunctionIndices));
        FileUnitStoreCommitter.WriteAside(UnitStoreFiles.PlansPath(location), stream => stream.Write(kept.Plans));
        FileUnitStoreCommitter.WriteAside(UnitStoreFiles.FactsPath(location), stream => stream.Write(kept.Facts));
    }
}

/// <summary>Desktop: the kept compile's files beside the units.</summary>
internal sealed class FileUnitKeptCompileLoader : IUnitKeptCompileLoader
{
    public UnitKeptCompile? Load(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        try
        {
            return new(File.ReadAllBytes(UnitStoreFiles.ModulePath(location)),
                File.ReadAllBytes(UnitStoreFiles.FunctionIndicesPath(location)),
                File.ReadAllBytes(UnitStoreFiles.PlansPath(location)),
                File.ReadAllBytes(UnitStoreFiles.FactsPath(location)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Desktop committing: the new segment and the manifest are each written in one sequential
/// pass beside their final name and moved into place, so a reader never sees a partial file;
/// then the segments the manifest no longer lists are deleted. A segment another process
/// still holds stays for the next compile to delete, and the summary names it.
/// </summary>
internal sealed class FileUnitStoreCommitter(IUnitFileDeleter files) : IUnitStoreCommitter
{
    private readonly IUnitFileDeleter _files = files ?? throw new ArgumentNullException(nameof(files));

    public void Commit(string location, UnitStoreChange change)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        var directory = UnitStoreFiles.Directory(location);
        System.IO.Directory.CreateDirectory(directory);
        var summary = change.Summary;
        if (change.Manifest is { } manifest)
        {
            if (change.Segment is { } segment)
                WriteAside(UnitStoreFiles.Segment(directory, change.Sequence), segment.WriteTo);
            WriteAside(UnitStoreFiles.Manifest(directory), stream => stream.Write(manifest));
            var stale = new List<string>();
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "units-*.seg"))
            {
                if (!UnitStoreFiles.TryParseSequence(file, out var sequence) ||
                    !change.LiveSequences.Contains(sequence))
                {
                    stale.Add(file);
                }
            }
            // The single-file store of earlier versions.
            stale.Add(Path.Combine(directory, "units.bin"));
            foreach (var file in stale)
            {
                try
                {
                    _files.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    summary += $"kept {Path.GetFileName(file)}: another process holds it\n";
                }
            }
        }
        File.WriteAllText(Path.Combine(directory, "last-compile.txt"), summary);
    }

    internal static void WriteAside(string path, Action<Stream> write)
    {
        var staging = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None,
                   1 << 16, FileOptions.SequentialScan))
        {
            write(stream);
        }
        File.Move(staging, path, overwrite: true);
    }
}

internal interface IUnitFileDeleter
{
    // Deletes a file, if there is one; throws IOException or UnauthorizedAccessException
    // when another process holds it.
    void Delete(string path);
}

internal sealed class UnitFileDeleter : IUnitFileDeleter
{
    public void Delete(string path) => File.Delete(path);
}

/// <summary>A segment file mapped read-only into the address space.</summary>
internal sealed unsafe class MappedUnitSegmentMemory : UnitSegmentMemory
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _pointer;
    private readonly long _length;
    private bool _acquired;

    public MappedUnitSegmentMemory(string path, IUnitMemoryPrefetcher prefetcher)
    {
        ArgumentNullException.ThrowIfNull(prefetcher);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 1, FileOptions.None);
        MemoryMappedFile? file = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            _length = stream.Length;
            if (_length == 0)
                throw new InvalidDataException("A unit segment is empty.");
            file = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read,
                HandleInheritability.None, leaveOpen: false);
            view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* address = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref address);
            _acquired = true;
            _pointer = address + view.PointerOffset;
            _ = prefetcher.Prefetch(address, (nuint)(_length + view.PointerOffset));
        }
        catch
        {
            if (_acquired) view!.SafeMemoryMappedViewHandle.ReleasePointer();
            view?.Dispose();
            if (file is null) stream.Dispose();
            file?.Dispose();
            throw;
        }
        _file = file;
        _view = view;
    }

    public override byte* Pointer => _pointer;
    public override long Length => _length;

    public override void Dispose()
    {
        if (_acquired)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _acquired = false;
        }
        _view.Dispose();
        _file.Dispose();
    }
}

/// <summary>
/// Asks the OS to read a mapped range ahead, so a cold segment streams in sequentially
/// instead of faulting page by page.
/// </summary>
internal unsafe interface IUnitMemoryPrefetcher
{
    // Whether the OS took the request; a mapping reads on demand either way.
    bool Prefetch(byte* address, nuint length);
}

/// <summary>madvise(MADV_WILLNEED), on Linux and macOS.</summary>
internal sealed unsafe class AdvisingUnitMemoryPrefetcher(nint madvise) : IUnitMemoryPrefetcher
{
    private const int WillNeed = 3;

    public bool Prefetch(byte* address, nuint length) =>
        ((delegate* unmanaged<void*, nuint, int, int>)madvise)(address, length, WillNeed) == 0;
}

/// <summary>PrefetchVirtualMemory, on Windows, for the current process.</summary>
internal sealed unsafe class VirtualMemoryUnitMemoryPrefetcher(nint prefetchVirtualMemory) : IUnitMemoryPrefetcher
{
    // The current process's pseudo-handle.
    private const nint CurrentProcess = -1;

    public bool Prefetch(byte* address, nuint length)
    {
        var range = new MemoryRange { Address = address, Size = length };
        return ((delegate* unmanaged[Stdcall]<nint, nuint, MemoryRange*, uint, int>)prefetchVirtualMemory)(
            CurrentProcess, 1, &range, 0) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryRange
    {
        public void* Address;
        public nuint Size;
    }
}

/// <summary>A host with neither reads on demand.</summary>
internal sealed unsafe class NoUnitMemoryPrefetcher : IUnitMemoryPrefetcher
{
    public bool Prefetch(byte* address, nuint length) => false;
}

/// <summary>A native function by name, in the first of its libraries that loads.</summary>
internal sealed record NativeExport(ImmutableArray<string> Libraries, string Name);

internal interface INativeExportResolver
{
    // The export's address, or 0 when none of its libraries has it.
    nint Resolve(NativeExport export);
}

internal sealed class NativeLibraryExportResolver : INativeExportResolver
{
    public nint Resolve(NativeExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        foreach (var library in export.Libraries)
        {
            if (NativeLibrary.TryLoad(library, out var handle) &&
                NativeLibrary.TryGetExport(handle, export.Name, out var function))
            {
                return function;
            }
        }
        return 0;
    }
}

internal interface IUnitMemoryPrefetcherFactory
{
    // The prefetcher for a platform: its OS call, when the OS has it.
    IUnitMemoryPrefetcher Create(OSPlatform platform);
}

internal sealed class UnitMemoryPrefetcherFactory(INativeExportResolver exports) : IUnitMemoryPrefetcherFactory
{
    private readonly INativeExportResolver _exports = exports ?? throw new ArgumentNullException(nameof(exports));

    // Each platform's call, and the prefetcher that makes it.
    internal static readonly ImmutableDictionary<OSPlatform, (NativeExport Export, Func<nint, IUnitMemoryPrefetcher> Create)> Platforms =
        ImmutableDictionary<OSPlatform, (NativeExport, Func<nint, IUnitMemoryPrefetcher>)>.Empty
            .Add(OSPlatform.Linux, (new(["libc.so.6", "libc"], "madvise"), function => new AdvisingUnitMemoryPrefetcher(function)))
            .Add(OSPlatform.OSX, (new(["/usr/lib/libSystem.B.dylib"], "madvise"), function => new AdvisingUnitMemoryPrefetcher(function)))
            .Add(OSPlatform.Windows, (new(["kernel32.dll"], "PrefetchVirtualMemory"),
                function => new VirtualMemoryUnitMemoryPrefetcher(function)));

    // The platform this process runs on, or another when it is none of them.
    public static OSPlatform Host() => Platforms.Keys.FirstOrDefault(RuntimeInformation.IsOSPlatform, OSPlatform.Create("OTHER"));

    public IUnitMemoryPrefetcher Create(OSPlatform platform) =>
        Platforms.TryGetValue(platform, out var call) && _exports.Resolve(call.Export) is var function and not 0
            ? call.Create(function)
            : new NoUnitMemoryPrefetcher();
}

/// <summary>
/// Stores kept in memory, by location: for hosts without files or mapping (the playground,
/// which persists the bytes to IndexedDB) and for tests. Segments are pinned arrays, so they
/// are read in place like mapped files.
/// </summary>
internal sealed class UnitStoreMemory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, byte[]> _manifests = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Location, int Sequence), byte[]> _segments = [];
    private readonly Dictionary<string, string> _summaries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _inputReports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnitKeptCompile> _kept = new(StringComparer.Ordinal);
    // Locations whose compile committed units since it last recorded its inputs. A compile the
    // early cutoff serves commits none; its account is the one its inputs keep.
    private readonly HashSet<string> _committed = new(StringComparer.Ordinal);
    private const uint ArchiveMagic = 0x41554E4E; // "NNUA"
    private const int ArchiveVersion = 4;

    internal byte[]? Manifest(string location)
    {
        lock (_gate)
            return _manifests.GetValueOrDefault(location);
    }

    internal byte[]? Segment(string location, int sequence)
    {
        lock (_gate)
            return _segments.GetValueOrDefault((location, sequence));
    }

    internal string? Summary(string location)
    {
        lock (_gate)
            return _summaries.GetValueOrDefault(location);
    }

    internal UnitKeptCompile? Kept(string location)
    {
        lock (_gate)
            return _kept.GetValueOrDefault(location);
    }

    internal void Keep(string location, UnitKeptCompile kept)
    {
        lock (_gate)
            _kept[location] = kept;
    }

    internal byte[]? Inputs(string location)
    {
        lock (_gate)
            return _inputs.GetValueOrDefault(location);
    }

    internal string? InputReport(string location)
    {
        lock (_gate)
            return _inputReports.GetValueOrDefault(location);
    }

    internal void SetInputs(string location, byte[] inputs, string report)
    {
        lock (_gate)
        {
            _inputs[location] = inputs;
            _inputReports[location] = report;
            if (!_committed.Remove(location)) _summaries[location] = report;
        }
    }

    // A location's units and inputs as one array, for a host to keep (a browser's IndexedDB).
    internal byte[] Export(string location)
    {
        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(ArchiveMagic);
                writer.Write(ArchiveVersion);
                Bytes(writer, _manifests.GetValueOrDefault(location));
                Bytes(writer, _inputs.GetValueOrDefault(location));
                var kept = _kept.GetValueOrDefault(location);
                Bytes(writer, kept?.Module);
                Bytes(writer, kept?.FunctionIndices);
                Bytes(writer, kept?.Plans);
                Bytes(writer, kept?.Facts);
                var segments = new List<(int Sequence, byte[] Bytes)>();
                foreach (var ((owner, sequence), bytes) in _segments)
                {
                    if (owner == location) segments.Add((sequence, bytes));
                }
                writer.Write(segments.Count);
                foreach (var (sequence, bytes) in segments)
                {
                    writer.Write(sequence);
                    Bytes(writer, bytes);
                }
            }
            return stream.ToArray();
        }
    }

    // Replaces a location's units and inputs with an exported archive's; an archive of
    // another format leaves the location empty, as the units would not be read anyway.
    internal void Import(string location, ReadOnlySpan<byte> archive)
    {
        byte[]? manifest = null, inputs = null, module = null, functions = null, plans = null, facts = null;
        var segments = new List<(int Sequence, byte[] Bytes)>();
        try
        {
            using var stream = new MemoryStream(archive.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
            if (reader.ReadUInt32() == ArchiveMagic && reader.ReadInt32() == ArchiveVersion)
            {
                manifest = Bytes(reader, pinned: false);
                inputs = Bytes(reader, pinned: false);
                module = Bytes(reader, pinned: false);
                functions = Bytes(reader, pinned: false);
                plans = Bytes(reader, pinned: false);
                facts = Bytes(reader, pinned: false);
                var count = reader.ReadInt32();
                for (var index = 0; index < count; index++)
                {
                    var sequence = reader.ReadInt32();
                    segments.Add((sequence, Bytes(reader, pinned: true) ??
                        throw new InvalidDataException("An archived segment is missing.")));
                }
            }
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException or IOException)
        {
            manifest = null;
            inputs = null;
            module = null;
            functions = null;
            plans = null;
            facts = null;
            segments.Clear();
        }
        lock (_gate)
        {
            foreach (var key in new List<(string Location, int Sequence)>(_segments.Keys))
            {
                if (key.Location == location) _segments.Remove(key);
            }
            _manifests.Remove(location);
            _inputs.Remove(location);
            _inputReports.Remove(location);
            _kept.Remove(location);
            if (manifest is null) return;
            _manifests[location] = manifest;
            if (inputs is not null) _inputs[location] = inputs;
            if (module is not null && functions is not null && plans is not null && facts is not null)
                _kept[location] = new(module, functions, plans, facts);
            foreach (var (sequence, bytes) in segments) _segments[(location, sequence)] = bytes;
        }
    }

    private static void Bytes(BinaryWriter writer, byte[]? bytes)
    {
        writer.Write(bytes?.Length ?? -1);
        if (bytes is not null) writer.Write(bytes);
    }

    // Segments are read in place, so they live on the pinned heap.
    private static byte[]? Bytes(BinaryReader reader, bool pinned)
    {
        var length = reader.ReadInt32();
        if (length < 0) return null;
        if (length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("An archive is truncated.");
        var bytes = pinned ? GC.AllocateUninitializedArray<byte>(length, pinned: true) : new byte[length];
        reader.BaseStream.ReadExactly(bytes);
        return bytes;
    }

    internal void Apply(string location, UnitStoreChange change)
    {
        byte[]? segment = null;
        if (change is { Manifest: not null, Segment: { } writer })
        {
            segment = GC.AllocateUninitializedArray<byte>(checked((int)writer.Length), pinned: true);
            using var stream = new MemoryStream(segment, writable: true);
            writer.WriteTo(stream);
        }
        lock (_gate)
        {
            _summaries[location] = change.Summary;
            _committed.Add(location);

            if (change.Manifest is null)
                return;
            if (segment is not null)
                _segments[(location, change.Sequence)] = segment;
            _manifests[location] = change.Manifest;
            foreach (var key in new List<(string Location, int Sequence)>(_segments.Keys))
            {
                if (key.Location == location && !change.LiveSequences.Contains(key.Sequence))
                    _segments.Remove(key);
            }
        }
    }
}

internal sealed class MemoryUnitStoreLoader(UnitStoreMemory memory) : IUnitStoreLoader
{
    public UnitStore Load(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        return memory.Manifest(location) is { } manifest
            ? UnitStore.Over(manifest, sequence =>
                memory.Segment(location, sequence) is { } bytes ? new PinnedUnitSegmentMemory(bytes) : null)
            : UnitStore.Empty;
    }

}

internal sealed class MemoryUnitKeptCompileCommitter(UnitStoreMemory memory) : IUnitKeptCompileCommitter
{
    public void Commit(string location, UnitKeptCompile kept)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentNullException.ThrowIfNull(kept);
        memory.Keep(location, kept);
    }
}

internal sealed class MemoryUnitKeptCompileLoader(UnitStoreMemory memory) : IUnitKeptCompileLoader
{
    public UnitKeptCompile? Load(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        return memory.Kept(location);
    }
}

internal static class UnitMemoryServiceCollectionExtensions
{
    // Keeps a compile's units, inputs and kept compile in memory, as a browser session does.
    public static IServiceCollection UseUnitMemory(this IServiceCollection services, UnitStoreMemory memory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(memory);
        services.Replace(ServiceDescriptor.Singleton<IUnitStoreLoader>(new MemoryUnitStoreLoader(memory)));
        services.Replace(ServiceDescriptor.Singleton<IUnitStoreCommitter>(new MemoryUnitStoreCommitter(memory)));
        services.Replace(ServiceDescriptor.Singleton<IUnitKeptCompileLoader>(new MemoryUnitKeptCompileLoader(memory)));
        services.Replace(ServiceDescriptor.Singleton<IUnitKeptCompileCommitter>(new MemoryUnitKeptCompileCommitter(memory)));
        services.Replace(ServiceDescriptor.Singleton<IUnitInputsLoader>(new MemoryUnitInputsLoader(memory)));
        services.Replace(ServiceDescriptor.Singleton<IUnitInputsCommitter>(new MemoryUnitInputsCommitter(memory)));
        return services;
    }
}

internal sealed class MemoryUnitStoreCommitter(UnitStoreMemory memory) : IUnitStoreCommitter
{
    public void Commit(string location, UnitStoreChange change)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        memory.Apply(location, change);
    }
}

/// <summary>A segment held in an array on the pinned object heap.</summary>
internal sealed unsafe class PinnedUnitSegmentMemory(byte[] pinned) : UnitSegmentMemory
{
    // The array never moves: UnitStoreMemory allocates it pinned.
    public override byte* Pointer => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(pinned));
    public override long Length => pinned.Length;

    public override void Dispose()
    {
    }
}

/// <summary>Reads the inputs the last compile kept beside its units.</summary>
internal interface IUnitInputsLoader
{
    // The kept inputs' bytes, or null when there are none.
    byte[]? Load(string location);
}

/// <summary>Keeps a compile's inputs beside its units, with a plain report of their changes.</summary>
internal interface IUnitInputsCommitter
{
    void Commit(string location, byte[] inputs, string report);
}

internal sealed class FileUnitInputsLoader : IUnitInputsLoader
{
    public byte[]? Load(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        var path = UnitStoreFiles.InputsPath(location);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

internal sealed class FileUnitInputsCommitter : IUnitInputsCommitter
{
    public void Commit(string location, byte[] inputs, string report)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentNullException.ThrowIfNull(inputs);
        var path = UnitStoreFiles.InputsPath(location);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "last-inputs.txt"), report);
        // Written aside and moved into place, so a reader never sees a partial file.
        var staging = path + "." + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmp";
        File.WriteAllBytes(staging, inputs);
        File.Move(staging, path, overwrite: true);
    }
}

internal sealed class MemoryUnitInputsLoader(UnitStoreMemory memory) : IUnitInputsLoader
{
    public byte[]? Load(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        return memory.Inputs(location);
    }
}

internal sealed class MemoryUnitInputsCommitter(UnitStoreMemory memory) : IUnitInputsCommitter
{
    public void Commit(string location, byte[] inputs, string report)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentNullException.ThrowIfNull(inputs);
        memory.SetInputs(location, inputs, report);
    }
}
