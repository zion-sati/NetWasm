namespace System.Runtime.InteropServices.WebAssembly
{
    using System.Collections.Generic;
    using System.IO;

    // The one-verb contracts NetWasm's WASI platform composes its facades from.

    internal interface IStandardOutputWriter
    {
        void Write(ReadOnlySpan<byte> bytes);
    }

    internal interface IStandardErrorWriter
    {
        void Write(ReadOnlySpan<byte> bytes);
    }

    internal interface IStandardInputReader
    {
        int Read(Span<byte> destination);
    }

    internal interface ITerminalDetector
    {
        bool IsTerminal(StandardStream stream);
    }

    internal interface ISystemClock
    {
        DateTime GetUtcNow();
    }

    internal interface IMonotonicClock
    {
        long GetTimestamp();
    }

    internal interface ICommandLineSource
    {
        string[] GetArguments();
    }

    internal interface IEnvironmentSource
    {
        KeyValuePair<string, string>[] GetVariables();
    }

    // FileStream's constructors, File.Open and the other ways a file is opened.
    internal interface IFileOpener
    {
        // Opens or creates the file as the mode says, for the access asked; file is its handle.
        FileSystemError Open(string path, FileMode mode, FileAccess access, FileOptions options, out long file);
    }

    internal interface IFileCloser
    {
        void Close(long file);
    }

    internal interface IFileReader
    {
        // Copies the file's bytes from the offset into the destination; 0 bytes at its end.
        FileSystemError Read(long file, long offset, Span<byte> destination, out int bytesRead);
    }

    internal interface IFileWriter
    {
        // Writes every byte of the source at the offset, extending the file as needed.
        FileSystemError Write(long file, long offset, ReadOnlySpan<byte> source);
    }

    internal interface IFileLengthReader
    {
        FileSystemError GetLength(long file, out long length);
    }

    internal interface IFileLengthSetter
    {
        FileSystemError SetLength(long file, long length);
    }

    internal interface IFileFlusher
    {
        // Makes what was written durable (FileStream.Flush(true)).
        FileSystemError Flush(long file);
    }

    // File.Exists, Directory.Exists, FileInfo and DirectoryInfo.
    internal interface IFileStatusReader
    {
        // followLinks: the status of a symbolic link's target rather than the link's own.
        FileSystemError GetStatus(string path, bool followLinks, out FileSystemEntryStatus status);
    }

    internal interface IDirectoryCreator
    {
        FileSystemError CreateDirectory(string path);
    }

    internal interface IDirectoryRemover
    {
        // Removes an empty directory.
        FileSystemError RemoveDirectory(string path);
    }

    internal interface IFileDeleter
    {
        FileSystemError DeleteFile(string path);
    }

    internal interface IFileSystemEntryMover
    {
        // Renames a file or directory, replacing a file already at the destination.
        FileSystemError Move(string sourcePath, string destinationPath);
    }

    internal interface IDirectoryEntryReader
    {
        // The directory's entries, without "." and "..".
        FileSystemError ReadEntries(string path, out DirectoryEntry[] entries);
    }

    internal interface ISymbolicLinkReader
    {
        FileSystemError ReadLink(string path, out string target);
    }

    internal interface ISymbolicLinkCreator
    {
        FileSystemError CreateSymbolicLink(string path, string target);
    }

    internal interface IFileTimesSetter
    {
        // A null time is left unchanged.
        FileSystemError SetTimes(string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc);
    }

    // Environment.CurrentDirectory's starting value, against which relative paths resolve.
    internal interface IWorkingDirectorySource
    {
        // The absolute directory the program starts in, or null for the root.
        string? GetInitialDirectory();
    }
}
