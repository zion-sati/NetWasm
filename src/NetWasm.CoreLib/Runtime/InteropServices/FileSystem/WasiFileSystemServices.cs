namespace System.Runtime.InteropServices.FileSystem;

using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WebAssembly;

// NetWasm's WASI 0.3 bindings of the file and directory platform services. Paths resolve
// against the preopened directories; a file handle is the file's WASI descriptor. Outside
// every preopen nothing exists, except the directories above them, which the program can read
// but not change.

internal static class WasiFileHandles
{
    // The descriptor a file handle holds, or 0 for one no WASI opener returned.
    internal static int Descriptor(long file) => file is > 0 and <= int.MaxValue ? unchecked((int)file) : 0;
}

internal sealed class WasiFileOpener(IWasiPathResolver paths, IWasiOpenAtCall openAt, IWasiStatCall stat, IWasiDescriptorDropCall drop) :
    IFileOpener
{
    private const byte Create = 1;
    private const byte Exclusive = 4;
    private const byte Truncate = 8;
    private const byte ReadFlag = 1;
    private const byte WriteFlag = 2;
    private const byte FileIntegritySync = 4;

    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiOpenAtCall _openAt = openAt ?? throw new ArgumentNullException(nameof(openAt));
    private readonly IWasiStatCall _stat = stat ?? throw new ArgumentNullException(nameof(stat));
    private readonly IWasiDescriptorDropCall _drop = drop ?? throw new ArgumentNullException(nameof(drop));

    public FileSystemError Open(string path, FileMode mode, FileAccess access, FileOptions options, out long file)
    {
        file = 0;
        var openFlags = mode switch
        {
            FileMode.CreateNew => (byte)(Create | Exclusive),
            FileMode.Create => (byte)(Create | Truncate),
            FileMode.Open => (byte)0,
            FileMode.OpenOrCreate or FileMode.Append => Create,
            FileMode.Truncate => Truncate,
            _ => byte.MaxValue,
        };
        var descriptorFlags = access switch
        {
            FileAccess.Read => ReadFlag,
            FileAccess.Write => WriteFlag,
            FileAccess.ReadWrite => (byte)(ReadFlag | WriteFlag),
            _ => byte.MaxValue,
        };
        if (openFlags == byte.MaxValue || descriptorFlags == byte.MaxValue)
        {
            return FileSystemError.InvalidArgument;
        }

        if ((options & FileOptions.WriteThrough) != 0)
        {
            descriptorFlags |= FileIntegritySync;
        }

        var resolved = _paths.Resolve(path);
        if (resolved.Kind != WasiPathKind.Mapped)
        {
            return resolved.Kind == WasiPathKind.Ancestor ? FileSystemError.IsDirectory
                : (openFlags & Create) != 0 ? FileSystemError.AccessDenied
                : FileSystemError.NotFound;
        }

        var error = _openAt.OpenAt(resolved.Directory, resolved.Relative, openFlags, descriptorFlags, out var descriptor);
        if (error != FileSystemError.None)
        {
            return error;
        }

        // A directory opens for reading, but a file stream refuses it, as on Unix.
        error = _stat.Stat(descriptor, out var status);
        if (error == FileSystemError.None && status.Kind == FileSystemEntryKind.Directory)
        {
            error = FileSystemError.IsDirectory;
        }

        if (error != FileSystemError.None)
        {
            _drop.Drop(descriptor);
            return error;
        }

        file = descriptor;
        return FileSystemError.None;
    }
}

internal sealed class WasiFileCloser(IWasiDescriptorDropCall drop) : IFileCloser
{
    private readonly IWasiDescriptorDropCall _drop = drop ?? throw new ArgumentNullException(nameof(drop));

    public void Close(long file)
    {
        var descriptor = WasiFileHandles.Descriptor(file);
        if (descriptor != 0)
        {
            _drop.Drop(descriptor);
        }
    }
}

internal sealed class WasiFileReader(IWasiFileReadCall read) : IFileReader
{
    private readonly IWasiFileReadCall _read = read ?? throw new ArgumentNullException(nameof(read));

    public FileSystemError Read(long file, long offset, Span<byte> destination, out int bytesRead)
    {
        var descriptor = WasiFileHandles.Descriptor(file);
        bytesRead = 0;
        return descriptor == 0 ? FileSystemError.BadHandle : _read.Read(descriptor, offset, destination, out bytesRead);
    }
}

internal sealed class WasiFileWriter(IWasiFileWriteCall write) : IFileWriter
{
    private readonly IWasiFileWriteCall _write = write ?? throw new ArgumentNullException(nameof(write));

    public FileSystemError Write(long file, long offset, ReadOnlySpan<byte> source)
    {
        var descriptor = WasiFileHandles.Descriptor(file);
        return descriptor == 0 ? FileSystemError.BadHandle : _write.Write(descriptor, offset, source);
    }
}

internal sealed class WasiFileLengthReader(IWasiStatCall stat) : IFileLengthReader
{
    private readonly IWasiStatCall _stat = stat ?? throw new ArgumentNullException(nameof(stat));

    public FileSystemError GetLength(long file, out long length)
    {
        length = 0;
        var descriptor = WasiFileHandles.Descriptor(file);
        if (descriptor == 0)
        {
            return FileSystemError.BadHandle;
        }

        var error = _stat.Stat(descriptor, out var status);
        length = status.Length;
        return error;
    }
}

internal sealed class WasiFileLengthSetter(IWasiSetSizeCall setSize) : IFileLengthSetter
{
    private readonly IWasiSetSizeCall _setSize = setSize ?? throw new ArgumentNullException(nameof(setSize));

    public FileSystemError SetLength(long file, long length)
    {
        var descriptor = WasiFileHandles.Descriptor(file);
        return descriptor == 0 ? FileSystemError.BadHandle
            : length < 0 ? FileSystemError.InvalidArgument
            : _setSize.SetSize(descriptor, length);
    }
}

internal sealed class WasiFileFlusher(IWasiSyncCall sync) : IFileFlusher
{
    private readonly IWasiSyncCall _sync = sync ?? throw new ArgumentNullException(nameof(sync));

    public FileSystemError Flush(long file)
    {
        var descriptor = WasiFileHandles.Descriptor(file);
        return descriptor == 0 ? FileSystemError.BadHandle : _sync.Sync(descriptor);
    }
}

internal sealed class WasiFileStatusReader(IWasiPathResolver paths, IWasiStatAtCall statAt) : IFileStatusReader
{
    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiStatAtCall _statAt = statAt ?? throw new ArgumentNullException(nameof(statAt));

    public FileSystemError GetStatus(string path, bool followLinks, out FileSystemEntryStatus status)
    {
        var resolved = _paths.Resolve(path);
        switch (resolved.Kind)
        {
            case WasiPathKind.Mapped:
                return _statAt.StatAt(resolved.Directory, resolved.Relative, followLinks, out status);
            case WasiPathKind.Ancestor:
                status = new FileSystemEntryStatus(
                    FileSystemEntryKind.Directory, 0, DateTime.UnixEpoch, DateTime.UnixEpoch, DateTime.UnixEpoch);
                return FileSystemError.None;
            default:
                status = default;
                return FileSystemError.NotFound;
        }
    }
}

// create-directory-at, remove-directory-at or unlink-file-at on a path inside a preopen; a
// mount point or a directory above one cannot change.
internal abstract class WasiPathService(IWasiPathResolver paths, IWasiPathCall call)
{
    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiPathCall _call = call ?? throw new ArgumentNullException(nameof(call));

    private protected FileSystemError Invoke(string path, FileSystemError fixedDirectory, FileSystemError unmapped)
    {
        var resolved = _paths.Resolve(path);
        return resolved.Kind switch
        {
            WasiPathKind.Mapped when resolved.Relative != "." => _call.Invoke(resolved.Directory, resolved.Relative),
            WasiPathKind.Unmapped => unmapped,
            _ => fixedDirectory,
        };
    }
}

internal sealed class WasiDirectoryCreator(IWasiPathResolver paths, IWasiPathCall createDirectoryAt) :
    WasiPathService(paths, createDirectoryAt), IDirectoryCreator
{
    public FileSystemError CreateDirectory(string path) =>
        Invoke(path, FileSystemError.AlreadyExists, FileSystemError.AccessDenied);
}

internal sealed class WasiDirectoryRemover(IWasiPathResolver paths, IWasiPathCall removeDirectoryAt) :
    WasiPathService(paths, removeDirectoryAt), IDirectoryRemover
{
    public FileSystemError RemoveDirectory(string path) =>
        Invoke(path, FileSystemError.AccessDenied, FileSystemError.NotFound);
}

internal sealed class WasiFileDeleter(IWasiPathResolver paths, IWasiPathCall unlinkFileAt) :
    WasiPathService(paths, unlinkFileAt), IFileDeleter
{
    public FileSystemError DeleteFile(string path) =>
        Invoke(path, FileSystemError.IsDirectory, FileSystemError.NotFound);
}

internal sealed class WasiFileSystemEntryMover(IWasiPathResolver paths, IWasiRenameAtCall renameAt) : IFileSystemEntryMover
{
    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiRenameAtCall _renameAt = renameAt ?? throw new ArgumentNullException(nameof(renameAt));

    public FileSystemError Move(string sourcePath, string destinationPath)
    {
        var source = _paths.Resolve(sourcePath);
        if (source.Kind == WasiPathKind.Unmapped)
        {
            return FileSystemError.NotFound;
        }

        var destination = _paths.Resolve(destinationPath);
        if (source.Kind != WasiPathKind.Mapped || source.Relative == "."
            || destination.Kind != WasiPathKind.Mapped || destination.Relative == ".")
        {
            return FileSystemError.AccessDenied;
        }

        return _renameAt.RenameAt(source.Directory, source.Relative, destination.Directory, destination.Relative);
    }
}

internal sealed class WasiDirectoryEntryReader(
    IWasiPathResolver paths,
    IWasiMountTable mounts,
    IWasiOpenAtCall openAt,
    IWasiDirectoryReadCall readDirectory,
    IWasiDescriptorDropCall drop) : IDirectoryEntryReader
{
    private const byte DirectoryFlag = 2;
    private const byte ReadFlag = 1;

    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiMountTable _mounts = mounts ?? throw new ArgumentNullException(nameof(mounts));
    private readonly IWasiOpenAtCall _openAt = openAt ?? throw new ArgumentNullException(nameof(openAt));
    private readonly IWasiDirectoryReadCall _readDirectory = readDirectory ?? throw new ArgumentNullException(nameof(readDirectory));
    private readonly IWasiDescriptorDropCall _drop = drop ?? throw new ArgumentNullException(nameof(drop));

    public FileSystemError ReadEntries(string path, out DirectoryEntry[] entries)
    {
        entries = [];
        var resolved = _paths.Resolve(path);
        if (resolved.Kind == WasiPathKind.Unmapped)
        {
            return FileSystemError.NotFound;
        }

        if (resolved.Kind == WasiPathKind.Ancestor)
        {
            entries = MountChildren(path.Length > 1 ? path.TrimEnd('/') + "/" : "/");
            return FileSystemError.None;
        }

        var error = _openAt.OpenAt(resolved.Directory, resolved.Relative, DirectoryFlag, ReadFlag, out var descriptor);
        if (error != FileSystemError.None)
        {
            return error;
        }

        error = _readDirectory.Read(descriptor, out entries);
        _drop.Drop(descriptor);
        return error;
    }

    // The next segment of every mount point below an ancestor directory.
    private DirectoryEntry[] MountChildren(string prefix)
    {
        var names = new List<string>();
        foreach (var mount in _mounts.Read())
        {
            if (mount.Path.Length > prefix.Length && mount.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                var end = mount.Path.IndexOf('/', prefix.Length);
                var name = end < 0 ? mount.Path[prefix.Length..] : mount.Path[prefix.Length..end];
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        var children = new DirectoryEntry[names.Count];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = new DirectoryEntry(names[index], FileSystemEntryKind.Directory);
        }

        return children;
    }
}

internal sealed class WasiSymbolicLinkReader(IWasiPathResolver paths, IWasiReadlinkAtCall readlinkAt) : ISymbolicLinkReader
{
    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiReadlinkAtCall _readlinkAt = readlinkAt ?? throw new ArgumentNullException(nameof(readlinkAt));

    public FileSystemError ReadLink(string path, out string target)
    {
        target = string.Empty;
        var resolved = _paths.Resolve(path);
        return resolved.Kind switch
        {
            WasiPathKind.Mapped => _readlinkAt.ReadlinkAt(resolved.Directory, resolved.Relative, out target),
            WasiPathKind.Ancestor => FileSystemError.InvalidArgument,
            _ => FileSystemError.NotFound,
        };
    }
}

internal sealed class WasiSymbolicLinkCreator(IWasiPathResolver paths, IWasiSymlinkAtCall symlinkAt) : ISymbolicLinkCreator
{
    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiSymlinkAtCall _symlinkAt = symlinkAt ?? throw new ArgumentNullException(nameof(symlinkAt));

    public FileSystemError CreateSymbolicLink(string path, string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var resolved = _paths.Resolve(path);
        return resolved.Kind switch
        {
            WasiPathKind.Mapped when resolved.Relative != "." => _symlinkAt.SymlinkAt(resolved.Directory, target, resolved.Relative),
            WasiPathKind.Unmapped => FileSystemError.AccessDenied,
            _ => FileSystemError.AlreadyExists,
        };
    }
}

internal sealed class WasiFileTimesSetter(IWasiPathResolver paths, IWasiSetTimesAtCall setTimesAt) : IFileTimesSetter
{
    private readonly IWasiPathResolver _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWasiSetTimesAtCall _setTimesAt = setTimesAt ?? throw new ArgumentNullException(nameof(setTimesAt));

    public FileSystemError SetTimes(string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc)
    {
        var resolved = _paths.Resolve(path);
        return resolved.Kind switch
        {
            WasiPathKind.Mapped => _setTimesAt.SetTimesAt(resolved.Directory, resolved.Relative, lastAccessTimeUtc, lastWriteTimeUtc),
            WasiPathKind.Ancestor => FileSystemError.AccessDenied,
            _ => FileSystemError.NotFound,
        };
    }
}

internal sealed class WasiWorkingDirectorySource(IWasiInitialDirectoryCall initialDirectory) : IWorkingDirectorySource
{
    private readonly IWasiInitialDirectoryCall _initialDirectory =
        initialDirectory ?? throw new ArgumentNullException(nameof(initialDirectory));

    public string? GetInitialDirectory() => _initialDirectory.Read();
}
