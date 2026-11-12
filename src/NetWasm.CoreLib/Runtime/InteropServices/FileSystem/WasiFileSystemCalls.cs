namespace System.Runtime.InteropServices.FileSystem;

using System.Runtime.InteropServices.Concurrency;
using System.Runtime.InteropServices.WebAssembly;
using Layout = WasiFileSystemLayout;

// The async descriptor methods with a path or a descriptor and a small result: each lowers its
// arguments, waits for the subtask to return and reads its result<_, error-code>.

internal abstract class WasiAsyncCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator)
{
    private protected readonly ISubtaskReturnAwaiter Subtasks = subtasks ?? throw new ArgumentNullException(nameof(subtasks));
    private protected readonly ICanonicalBufferAllocator Allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
    private protected static nuint P => Layout.Pointer;

    private protected CanonicalBuffer SmallResult() => Allocator.Allocate(Layout.SmallResultSize, P);
}

// descriptor, path-flags (symlink-follow), path, open-flags, descriptor-flags.
internal sealed class WasiOpenAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiOpenAtCall
{
    public FileSystemError OpenAt(int directory, string path, byte openFlags, byte descriptorFlags, out int descriptor)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var parameters = Allocator.Allocate(Layout.OpenAtParametersSize, P);
        CanonicalAbi.WriteInt32(parameters.Address, 0, directory);
        CanonicalAbi.WriteByte(parameters.Address, 4, 1);
        Layout.WriteString(parameters.Address + 8, lowered);
        CanonicalAbi.WriteByte(parameters.Address, 8 + P * 2, openFlags);
        CanonicalAbi.WriteByte(parameters.Address, 9 + P * 2, descriptorFlags);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.OpenAt(parameters.Address, result.Address));
        var error = Layout.ReadResult(result.Address, P);
        descriptor = error == FileSystemError.None ? CanonicalAbi.ReadInt32(result.Address, P) : 0;
        return error;
    }
}

internal sealed class WasiStatCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiStatCall
{
    public FileSystemError Stat(int descriptor, out FileSystemEntryStatus status)
    {
        using var result = Allocator.Allocate(Layout.StatResultSize, 8);
        Subtasks.AwaitReturn(WasiFileSystemImports.Stat(descriptor, result.Address));
        var error = Layout.ReadResult(result.Address, 8);
        status = error == FileSystemError.None ? Layout.ReadStat(result.Address + 8) : default;
        return error;
    }
}

internal sealed class WasiStatAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiStatAtCall
{
    public FileSystemError StatAt(int directory, string path, bool followLinks, out FileSystemEntryStatus status)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var result = Allocator.Allocate(Layout.StatResultSize, 8);
        Subtasks.AwaitReturn(WasiFileSystemImports.StatAt(
            directory, followLinks ? 1 : 0, lowered.Address, lowered.Length, result.Address));
        var error = Layout.ReadResult(result.Address, 8);
        status = error == FileSystemError.None ? Layout.ReadStat(result.Address + 8) : default;
        return error;
    }
}

internal sealed class WasiCreateDirectoryAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiPathCall
{
    public FileSystemError Invoke(int directory, string path)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.CreateDirectoryAt(directory, lowered.Address, lowered.Length, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

internal sealed class WasiRemoveDirectoryAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiPathCall
{
    public FileSystemError Invoke(int directory, string path)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.RemoveDirectoryAt(directory, lowered.Address, lowered.Length, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

internal sealed class WasiUnlinkFileAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiPathCall
{
    public FileSystemError Invoke(int directory, string path)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.UnlinkFileAt(directory, lowered.Address, lowered.Length, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

internal sealed class WasiReadlinkAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiReadlinkAtCall
{
    public FileSystemError ReadlinkAt(int directory, string path, out string target)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.ReadlinkAt(directory, lowered.Address, lowered.Length, result.Address));
        var error = Layout.ReadResult(result.Address, P);
        target = error == FileSystemError.None ? Layout.TakeString(result.Address + P) : string.Empty;
        return error;
    }
}

// descriptor, old-path at P, new-descriptor at 3P, new-path at 4P.
internal sealed class WasiRenameAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiRenameAtCall
{
    public FileSystemError RenameAt(int directory, string path, int newDirectory, string newPath)
    {
        using var source = CanonicalAbi.LowerString(path);
        using var destination = CanonicalAbi.LowerString(newPath);
        using var parameters = Allocator.Allocate(P * 6, P);
        CanonicalAbi.WriteInt32(parameters.Address, 0, directory);
        Layout.WriteString(parameters.Address + P, source);
        CanonicalAbi.WriteInt32(parameters.Address, P * 3, newDirectory);
        Layout.WriteString(parameters.Address + P * 4, destination);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.RenameAt(parameters.Address, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

// descriptor, old-path (the target) at P, new-path (the link) at 3P.
internal sealed class WasiSymlinkAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiSymlinkAtCall
{
    public FileSystemError SymlinkAt(int directory, string target, string path)
    {
        using var loweredTarget = CanonicalAbi.LowerString(target);
        using var loweredPath = CanonicalAbi.LowerString(path);
        using var parameters = Allocator.Allocate(P * 5, P);
        CanonicalAbi.WriteInt32(parameters.Address, 0, directory);
        Layout.WriteString(parameters.Address + P, loweredTarget);
        Layout.WriteString(parameters.Address + P * 3, loweredPath);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.SymlinkAt(parameters.Address, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

// descriptor, path-flags (symlink-follow), path at 8, then two new-timestamps (24 bytes,
// 8-aligned) at 8 + 2P.
internal sealed class WasiSetTimesAtCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiSetTimesAtCall
{
    public FileSystemError SetTimesAt(int directory, string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc)
    {
        using var lowered = CanonicalAbi.LowerString(path);
        using var parameters = Allocator.Allocate(P * 2 + 56, 8);
        CanonicalAbi.WriteInt32(parameters.Address, 0, directory);
        CanonicalAbi.WriteByte(parameters.Address, 4, 1);
        Layout.WriteString(parameters.Address + 8, lowered);
        Layout.WriteNewTimestamp(parameters.Address + 8 + P * 2, lastAccessTimeUtc);
        Layout.WriteNewTimestamp(parameters.Address + 32 + P * 2, lastWriteTimeUtc);
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.SetTimesAt(parameters.Address, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

internal sealed class WasiSetSizeCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiSetSizeCall
{
    public FileSystemError SetSize(int descriptor, long size)
    {
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.SetSize(descriptor, size, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

internal sealed class WasiSyncCall(ISubtaskReturnAwaiter subtasks, ICanonicalBufferAllocator allocator) :
    WasiAsyncCall(subtasks, allocator), IWasiSyncCall
{
    public FileSystemError Sync(int descriptor)
    {
        using var result = SmallResult();
        Subtasks.AwaitReturn(WasiFileSystemImports.Sync(descriptor, result.Address));
        return Layout.ReadResult(result.Address, P);
    }
}

internal sealed class WasiDescriptorDropCall : IWasiDescriptorDropCall
{
    public void Drop(int descriptor) => WasiFileSystemImports.DropDescriptor(descriptor);
}

// get-initial-cwd's option<string>: the discriminant, then the string at P.
internal sealed class WasiInitialDirectoryCall(ICanonicalBufferAllocator allocator) : IWasiInitialDirectoryCall
{
    private readonly ICanonicalBufferAllocator _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));

    public string? Read()
    {
        using var result = _allocator.Allocate(Layout.Pointer * 3, Layout.Pointer);
        WasiFileSystemImports.GetInitialCwd(result.Address);
        return CanonicalAbi.ReadByte(result.Address, 0) == 0 ? null : Layout.TakeString(result.Address + Layout.Pointer);
    }
}
