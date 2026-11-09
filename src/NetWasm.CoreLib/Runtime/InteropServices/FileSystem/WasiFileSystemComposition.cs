namespace System.Runtime.InteropServices.FileSystem;

using System.Runtime.InteropServices.Concurrency;
using System.Runtime.InteropServices.WebAssembly;

// Composes NetWasm's WASI file and directory services, each on its own first use, so a program
// pays only for the calls it reaches. They share one mount table, read from the host once.
internal static class WasiFileSystemComposition
{
    private static WasiMountTable? s_mounts;

    private static WasiMountTable Mounts => s_mounts ??=
        new WasiMountTable(new TimeZones.WasiPreopenedDirectorySource(new TimeZones.WasiPreopensInvoker()));

    private static WasiPathResolver Paths() => new(Mounts);

    private static SubtaskReturnAwaiter Subtasks() =>
        new(WasiPlatformServicesComposition.CreateWaitableEventAwaiter(), new WasiSubtaskDropInvoker());

    private static CanonicalBufferAllocator Allocator() => new();

    private static WasiOpenAtCall OpenAt() => new(Subtasks(), Allocator());

    private static WasiStatCall Stat() => new(Subtasks(), Allocator());

    internal static IFileOpener CreateFileOpener() =>
        new WasiFileOpener(Paths(), OpenAt(), Stat(), new WasiDescriptorDropCall());

    internal static IFileCloser CreateFileCloser() => new WasiFileCloser(new WasiDescriptorDropCall());

    internal static IFileReader CreateFileReader() =>
        new WasiFileReader(new WasiFileReadCall(WasiPlatformServicesComposition.CreateWaitableEventAwaiter(), Allocator()));

    internal static IFileWriter CreateFileWriter() =>
        new WasiFileWriter(new WasiFileWriteCall(WasiPlatformServicesComposition.CreateWaitableEventAwaiter(), Allocator()));

    internal static IFileLengthReader CreateFileLengthReader() => new WasiFileLengthReader(Stat());

    internal static IFileLengthSetter CreateFileLengthSetter() =>
        new WasiFileLengthSetter(new WasiSetSizeCall(Subtasks(), Allocator()));

    internal static IFileFlusher CreateFileFlusher() => new WasiFileFlusher(new WasiSyncCall(Subtasks(), Allocator()));

    internal static IFileStatusReader CreateFileStatusReader() =>
        new WasiFileStatusReader(Paths(), new WasiStatAtCall(Subtasks(), Allocator()));

    internal static IDirectoryCreator CreateDirectoryCreator() =>
        new WasiDirectoryCreator(Paths(), new WasiCreateDirectoryAtCall(Subtasks(), Allocator()));

    internal static IDirectoryRemover CreateDirectoryRemover() =>
        new WasiDirectoryRemover(Paths(), new WasiRemoveDirectoryAtCall(Subtasks(), Allocator()));

    internal static IFileDeleter CreateFileDeleter() =>
        new WasiFileDeleter(Paths(), new WasiUnlinkFileAtCall(Subtasks(), Allocator()));

    internal static IFileSystemEntryMover CreateFileSystemEntryMover() =>
        new WasiFileSystemEntryMover(Paths(), new WasiRenameAtCall(Subtasks(), Allocator()));

    internal static IDirectoryEntryReader CreateDirectoryEntryReader() =>
        new WasiDirectoryEntryReader(
            Paths(),
            Mounts,
            OpenAt(),
            new WasiDirectoryReadCall(WasiPlatformServicesComposition.CreateWaitableEventAwaiter(), Allocator()),
            new WasiDescriptorDropCall());

    internal static ISymbolicLinkReader CreateSymbolicLinkReader() =>
        new WasiSymbolicLinkReader(Paths(), new WasiReadlinkAtCall(Subtasks(), Allocator()));

    internal static ISymbolicLinkCreator CreateSymbolicLinkCreator() =>
        new WasiSymbolicLinkCreator(Paths(), new WasiSymlinkAtCall(Subtasks(), Allocator()));

    internal static IFileTimesSetter CreateFileTimesSetter() =>
        new WasiFileTimesSetter(Paths(), new WasiSetTimesAtCall(Subtasks(), Allocator()));

    internal static IWorkingDirectorySource CreateWorkingDirectorySource() =>
        new WasiWorkingDirectorySource(new WasiInitialDirectoryCall(Allocator()));
}
