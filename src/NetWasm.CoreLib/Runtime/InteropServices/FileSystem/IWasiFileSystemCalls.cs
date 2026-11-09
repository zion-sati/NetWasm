namespace System.Runtime.InteropServices.FileSystem;

using System.Runtime.InteropServices.WebAssembly;

// One wasi:filesystem call each, marshalled and awaited: the file and directory platform
// services compose these. A directory is a descriptor and a path relative to it.

internal interface IWasiOpenAtCall
{
    FileSystemError OpenAt(int directory, string path, byte openFlags, byte descriptorFlags, out int descriptor);
}

internal interface IWasiStatCall
{
    FileSystemError Stat(int descriptor, out FileSystemEntryStatus status);
}

internal interface IWasiStatAtCall
{
    FileSystemError StatAt(int directory, string path, bool followLinks, out FileSystemEntryStatus status);
}

// create-directory-at, remove-directory-at and unlink-file-at: a path, and nothing back.
internal interface IWasiPathCall
{
    FileSystemError Invoke(int directory, string path);
}

internal interface IWasiReadlinkAtCall
{
    FileSystemError ReadlinkAt(int directory, string path, out string target);
}

internal interface IWasiRenameAtCall
{
    FileSystemError RenameAt(int directory, string path, int newDirectory, string newPath);
}

internal interface IWasiSymlinkAtCall
{
    FileSystemError SymlinkAt(int directory, string target, string path);
}

internal interface IWasiSetTimesAtCall
{
    FileSystemError SetTimesAt(int directory, string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc);
}

internal interface IWasiSetSizeCall
{
    FileSystemError SetSize(int descriptor, long size);
}

internal interface IWasiSyncCall
{
    FileSystemError Sync(int descriptor);
}

internal interface IWasiDescriptorDropCall
{
    void Drop(int descriptor);
}

internal interface IWasiFileReadCall
{
    FileSystemError Read(int descriptor, long offset, Span<byte> destination, out int bytesRead);
}

internal interface IWasiFileWriteCall
{
    FileSystemError Write(int descriptor, long offset, ReadOnlySpan<byte> source);
}

internal interface IWasiDirectoryReadCall
{
    FileSystemError Read(int descriptor, out DirectoryEntry[] entries);
}

internal interface IWasiInitialDirectoryCall
{
    string? Read();
}
