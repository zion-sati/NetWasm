namespace System.Runtime.InteropServices.WebAssembly
{
    using System.IO;

    // The file system facade's types. IFileSystem mirrors the POSIX calls .NET makes on Unix, so
    // File, FileInfo, Directory, DirectoryInfo, FileStream and their enumeration port onto it with
    // upstream's shape. Paths are absolute ("/data/notes.txt"), with '/' separators and no "." or
    // ".." segments; a file handle is the value Open returns, from 0 to int.MaxValue. Each member
    // reports failure as a FileSystemError, which the file APIs turn into the exception .NET
    // throws on Unix for the matching errno.

    // Why a file or directory operation failed.
    public enum FileSystemError
    {
        None,
        NotFound,
        AccessDenied,
        AlreadyExists,
        NotDirectory,
        IsDirectory,
        NotEmpty,
        InvalidArgument,
        NameTooLong,
        Loop,
        ReadOnly,
        NoSpace,
        FileTooLarge,
        Busy,
        CrossDevice,
        BadHandle,
        Unsupported,
        IOError,
    }

    // What a path names.
    public enum FileSystemEntryKind
    {
        File,
        Directory,
        SymbolicLink,
        Other,
    }

    // A file system entry's status: what it is, its length and its times (UTC).
    public readonly struct FileSystemEntryStatus
    {
        public FileSystemEntryStatus(
            FileSystemEntryKind kind,
            long length,
            DateTime lastAccessTimeUtc,
            DateTime lastWriteTimeUtc,
            DateTime statusChangeTimeUtc)
        {
            Kind = kind;
            Length = length;
            LastAccessTimeUtc = lastAccessTimeUtc;
            LastWriteTimeUtc = lastWriteTimeUtc;
            StatusChangeTimeUtc = statusChangeTimeUtc;
        }

        public FileSystemEntryKind Kind { get; }
        public long Length { get; }
        public DateTime LastAccessTimeUtc { get; }
        public DateTime LastWriteTimeUtc { get; }
        public DateTime StatusChangeTimeUtc { get; }
    }

    // One entry of a directory: its name (no path) and what it is.
    public readonly struct DirectoryEntry
    {
        public DirectoryEntry(string name, FileSystemEntryKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }
        public FileSystemEntryKind Kind { get; }
    }

    public interface IFileSystem
    {
        // Opens or creates the file as the mode says, for the access asked; file is its handle. A
        // directory is IsDirectory.
        FileSystemError Open(string path, FileMode mode, FileAccess access, FileOptions options, out long file);

        void Close(long file);

        // Copies the file's bytes from the offset into the destination; 0 bytes at its end.
        FileSystemError Read(long file, long offset, Span<byte> destination, out int bytesRead);

        // Writes every byte of the source at the offset, extending the file as needed.
        FileSystemError Write(long file, long offset, ReadOnlySpan<byte> source);

        FileSystemError GetLength(long file, out long length);

        FileSystemError SetLength(long file, long length);

        // Makes what was written durable (FileStream.Flush(true)).
        FileSystemError Flush(long file);

        // followLinks: the status of a symbolic link's target rather than the link's own.
        FileSystemError GetStatus(string path, bool followLinks, out FileSystemEntryStatus status);

        FileSystemError CreateDirectory(string path);

        // Removes an empty directory.
        FileSystemError RemoveDirectory(string path);

        FileSystemError DeleteFile(string path);

        // Renames a file or directory, replacing a file already at the destination.
        FileSystemError Move(string sourcePath, string destinationPath);

        // The directory's entries, without "." and "..".
        FileSystemError ReadEntries(string path, out DirectoryEntry[] entries);

        FileSystemError ReadLink(string path, out string target);

        FileSystemError CreateSymbolicLink(string path, string target);

        // A null time is left unchanged.
        FileSystemError SetTimes(string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc);
    }

    // The file system facade's base: every member throws PlatformNotSupportedException until
    // overridden, so a platform implements only what its host supports.
    public abstract class PlatformFileSystem : IFileSystem
    {
        public virtual FileSystemError Open(string path, FileMode mode, FileAccess access, FileOptions options, out long file) =>
            throw PlatformServiceErrors.Unavailable("Opening a file", "FileSystem");

        public virtual void Close(long file) => throw PlatformServiceErrors.Unavailable("Closing a file", "FileSystem");

        public virtual FileSystemError Read(long file, long offset, Span<byte> destination, out int bytesRead) =>
            throw PlatformServiceErrors.Unavailable("Reading a file", "FileSystem");

        public virtual FileSystemError Write(long file, long offset, ReadOnlySpan<byte> source) =>
            throw PlatformServiceErrors.Unavailable("Writing a file", "FileSystem");

        public virtual FileSystemError GetLength(long file, out long length) =>
            throw PlatformServiceErrors.Unavailable("A file's length", "FileSystem");

        public virtual FileSystemError SetLength(long file, long length) =>
            throw PlatformServiceErrors.Unavailable("Setting a file's length", "FileSystem");

        public virtual FileSystemError Flush(long file) => throw PlatformServiceErrors.Unavailable("Flushing a file", "FileSystem");

        public virtual FileSystemError GetStatus(string path, bool followLinks, out FileSystemEntryStatus status) =>
            throw PlatformServiceErrors.Unavailable("A file system entry's status", "FileSystem");

        public virtual FileSystemError CreateDirectory(string path) => throw PlatformServiceErrors.Unavailable("Creating a directory", "FileSystem");

        public virtual FileSystemError RemoveDirectory(string path) => throw PlatformServiceErrors.Unavailable("Removing a directory", "FileSystem");

        public virtual FileSystemError DeleteFile(string path) => throw PlatformServiceErrors.Unavailable("Deleting a file", "FileSystem");

        public virtual FileSystemError Move(string sourcePath, string destinationPath) =>
            throw PlatformServiceErrors.Unavailable("Moving a file system entry", "FileSystem");

        public virtual FileSystemError ReadEntries(string path, out DirectoryEntry[] entries) =>
            throw PlatformServiceErrors.Unavailable("Reading a directory's entries", "FileSystem");

        public virtual FileSystemError ReadLink(string path, out string target) =>
            throw PlatformServiceErrors.Unavailable("Reading a symbolic link", "FileSystem");

        public virtual FileSystemError CreateSymbolicLink(string path, string target) =>
            throw PlatformServiceErrors.Unavailable("Creating a symbolic link", "FileSystem");

        public virtual FileSystemError SetTimes(string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc) =>
            throw PlatformServiceErrors.Unavailable("Setting a file's times", "FileSystem");
    }
}
