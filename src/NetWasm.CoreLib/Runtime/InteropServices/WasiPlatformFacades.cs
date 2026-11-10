namespace System.Runtime.InteropServices
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices.WebAssembly;

    // WasiPlatform's facades. Each composes NetWasm's WASI 0.3 building blocks on its members'
    // first use, so a program reaches only the WASI calls behind the members it uses.

    internal sealed class WasiStandardStreams : IStandardStreams
    {
        private IStandardOutputWriter? _output;
        private IStandardErrorWriter? _error;
        private IStandardInputReader? _input;
        private ITerminalDetector? _terminals;

        public void WriteOutput(ReadOnlySpan<byte> bytes) =>
            (_output ??= WasiPlatformServicesComposition.CreateStandardOutput()).Write(bytes);

        public void WriteError(ReadOnlySpan<byte> bytes) =>
            (_error ??= WasiPlatformServicesComposition.CreateStandardError()).Write(bytes);

        public int ReadInput(Span<byte> destination) =>
            (_input ??= WasiPlatformServicesComposition.CreateStandardInput()).Read(destination);

        public bool IsTerminal(StandardStream stream) =>
            (_terminals ??= new ConsoleServices.WasiTerminalDetector(
                new ConsoleServices.WasiTerminalInvoker(), new ConsoleServices.WasiTerminalDropInvoker())).IsTerminal(stream);
    }

    internal sealed class WasiClock : IClock
    {
        private ISystemClock? _system;
        private IMonotonicClock? _monotonic;

        public DateTime GetUtcNow() => (_system ??= new WasiUtcClock(new WasiUtcClockSource())).GetUtcNow();

        public long GetTimestamp() =>
            (_monotonic ??= new WasiMonotonicClock(new WasiMonotonicClockSource())).GetTimestamp();
    }

    internal sealed class WasiProcessEnvironment : IProcessEnvironment
    {
        private ICommandLineSource? _commandLine;
        private IEnvironmentSource? _environment;
        private IWorkingDirectorySource? _directory;

        public string[] GetArguments() =>
            (_commandLine ??= new CommandLine.WasiCommandLineArgumentSource(new CommandLine.WasiCommandLineInvoker()))
                .GetArguments();

        public KeyValuePair<string, string>[] GetVariables() =>
            (_environment ??= new TimeZones.WasiEnvironmentVariableSource(new TimeZones.WasiEnvironmentInvoker()))
                .GetVariables();

        public string? GetInitialDirectory() =>
            (_directory ??= FileSystem.WasiFileSystemComposition.CreateWorkingDirectorySource()).GetInitialDirectory();
    }

    internal sealed class WasiProcessExit : IProcessExit
    {
        // exit carries success; exit-with-code any other status, truncated to the eight bits a
        // process status has, as on Linux. Neither returns to a conforming host's caller.
        public void Exit(int exitCode)
        {
            if (exitCode == 0)
            {
                CommandLine.WasiExitImports.Exit(0);
            }
            else
            {
                CommandLine.WasiExitImports.ExitWithCode((byte)exitCode);
            }
        }
    }
}

namespace System.Runtime.InteropServices.FileSystem
{
    using System.IO;
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class WasiFileSystem : IFileSystem
    {
        private IFileOpener? _opener;
        private IFileCloser? _closer;
        private IFileReader? _reader;
        private IFileWriter? _writer;
        private IFileLengthReader? _lengthReader;
        private IFileLengthSetter? _lengthSetter;
        private IFileFlusher? _flusher;
        private IFileStatusReader? _statusReader;
        private IDirectoryCreator? _directoryCreator;
        private IDirectoryRemover? _directoryRemover;
        private IFileDeleter? _deleter;
        private IFileSystemEntryMover? _mover;
        private IDirectoryEntryReader? _entryReader;
        private ISymbolicLinkReader? _linkReader;
        private ISymbolicLinkCreator? _linkCreator;
        private IFileTimesSetter? _timesSetter;

        public FileSystemError Open(string path, FileMode mode, FileAccess access, FileOptions options, out long file) =>
            (_opener ??= WasiFileSystemComposition.CreateFileOpener()).Open(path, mode, access, options, out file);

        public void Close(long file) => (_closer ??= WasiFileSystemComposition.CreateFileCloser()).Close(file);

        public FileSystemError Read(long file, long offset, Span<byte> destination, out int bytesRead) =>
            (_reader ??= WasiFileSystemComposition.CreateFileReader()).Read(file, offset, destination, out bytesRead);

        public FileSystemError Write(long file, long offset, ReadOnlySpan<byte> source) =>
            (_writer ??= WasiFileSystemComposition.CreateFileWriter()).Write(file, offset, source);

        public FileSystemError GetLength(long file, out long length) =>
            (_lengthReader ??= WasiFileSystemComposition.CreateFileLengthReader()).GetLength(file, out length);

        public FileSystemError SetLength(long file, long length) =>
            (_lengthSetter ??= WasiFileSystemComposition.CreateFileLengthSetter()).SetLength(file, length);

        public FileSystemError Flush(long file) => (_flusher ??= WasiFileSystemComposition.CreateFileFlusher()).Flush(file);

        public FileSystemError GetStatus(string path, bool followLinks, out FileSystemEntryStatus status) =>
            (_statusReader ??= WasiFileSystemComposition.CreateFileStatusReader()).GetStatus(path, followLinks, out status);

        public FileSystemError CreateDirectory(string path) =>
            (_directoryCreator ??= WasiFileSystemComposition.CreateDirectoryCreator()).CreateDirectory(path);

        public FileSystemError RemoveDirectory(string path) =>
            (_directoryRemover ??= WasiFileSystemComposition.CreateDirectoryRemover()).RemoveDirectory(path);

        public FileSystemError DeleteFile(string path) => (_deleter ??= WasiFileSystemComposition.CreateFileDeleter()).DeleteFile(path);

        public FileSystemError Move(string sourcePath, string destinationPath) =>
            (_mover ??= WasiFileSystemComposition.CreateFileSystemEntryMover()).Move(sourcePath, destinationPath);

        public FileSystemError ReadEntries(string path, out DirectoryEntry[] entries) =>
            (_entryReader ??= WasiFileSystemComposition.CreateDirectoryEntryReader()).ReadEntries(path, out entries);

        public FileSystemError ReadLink(string path, out string target) =>
            (_linkReader ??= WasiFileSystemComposition.CreateSymbolicLinkReader()).ReadLink(path, out target);

        public FileSystemError CreateSymbolicLink(string path, string target) =>
            (_linkCreator ??= WasiFileSystemComposition.CreateSymbolicLinkCreator()).CreateSymbolicLink(path, target);

        public FileSystemError SetTimes(string path, DateTime? lastAccessTimeUtc, DateTime? lastWriteTimeUtc) =>
            (_timesSetter ??= WasiFileSystemComposition.CreateFileTimesSetter()).SetTimes(path, lastAccessTimeUtc, lastWriteTimeUtc);
    }
}
