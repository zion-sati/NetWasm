namespace System.Runtime.InteropServices.FileSystem;

using System.Runtime.InteropServices.WebAssembly;

// wasi:filesystem 0.3: the preopened directories and the descriptor methods the file and
// directory platform services call. Every async method is lowered async: its flat parameters
// pass directly when there are at most four, otherwise through a record in memory, and its
// result is stored at result; the call returns its subtask's status.
internal static class WasiFileSystemImports
{
    // Stores option<string> at result.
    [WitImport("wasi:cli/environment@0.3.2", "get-initial-cwd")]
    internal static extern void GetInitialCwd(nuint result);

    [WitImport("wasi:filesystem/types@0.3.2", "[resource-drop]descriptor")]
    internal static extern void DropDescriptor(int descriptor);

    // descriptor, path-flags, path, open-flags, descriptor-flags.
    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.open-at")]
    internal static extern int OpenAt(nuint parameters, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.stat")]
    internal static extern int Stat(int descriptor, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.stat-at")]
    internal static extern int StatAt(int descriptor, int pathFlags, nuint path, nuint pathLength, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.create-directory-at")]
    internal static extern int CreateDirectoryAt(int descriptor, nuint path, nuint pathLength, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.remove-directory-at")]
    internal static extern int RemoveDirectoryAt(int descriptor, nuint path, nuint pathLength, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.unlink-file-at")]
    internal static extern int UnlinkFileAt(int descriptor, nuint path, nuint pathLength, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.readlink-at")]
    internal static extern int ReadlinkAt(int descriptor, nuint path, nuint pathLength, nuint result);

    // descriptor, old-path, new-descriptor, new-path.
    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.rename-at")]
    internal static extern int RenameAt(nuint parameters, nuint result);

    // descriptor, old-path (the link's target), new-path (the link).
    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.symlink-at")]
    internal static extern int SymlinkAt(nuint parameters, nuint result);

    // descriptor, path-flags, path, data-access-timestamp, data-modification-timestamp.
    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.set-times-at")]
    internal static extern int SetTimesAt(nuint parameters, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.set-size")]
    internal static extern int SetSize(int descriptor, long size, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][method]descriptor.sync")]
    internal static extern int Sync(int descriptor, nuint result);

    // Stores the readable end of a stream<u8> and its future<result<_, error-code>> at result.
    [WitImport("wasi:filesystem/types@0.3.2", "[method]descriptor.read-via-stream")]
    internal static extern void ReadViaStream(int descriptor, long offset, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][stream-read-0][method]descriptor.read-via-stream")]
    internal static extern int ReadBytes(int readable, nuint buffer, nuint length);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[stream-drop-readable-0][method]descriptor.read-via-stream")]
    internal static extern void DropReadBytes(int readable);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][future-read-1][method]descriptor.read-via-stream")]
    internal static extern int ReadReadResult(int readable, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[future-drop-readable-1][method]descriptor.read-via-stream")]
    internal static extern void DropReadResult(int readable);

    // The readable end in the low 32 bits, the writable end in the high 32.
    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[stream-new-0][method]descriptor.write-via-stream")]
    internal static extern long CreateWriteStream();

    // Hands the host the readable end; returns the future<result<_, error-code>>.
    [WitImport("wasi:filesystem/types@0.3.2", "[method]descriptor.write-via-stream")]
    internal static extern int WriteViaStream(int descriptor, int readable, long offset);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][stream-write-0][method]descriptor.write-via-stream")]
    internal static extern int WriteBytes(int writable, nuint buffer, nuint length);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[stream-drop-writable-0][method]descriptor.write-via-stream")]
    internal static extern void DropWriteBytes(int writable);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][future-read-1][method]descriptor.write-via-stream")]
    internal static extern int ReadWriteResult(int readable, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[future-drop-readable-1][method]descriptor.write-via-stream")]
    internal static extern void DropWriteResult(int readable);

    // Stores the readable end of a stream<directory-entry> and its future at result.
    [WitImport("wasi:filesystem/types@0.3.2", "[method]descriptor.read-directory")]
    internal static extern void ReadDirectory(int descriptor, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][stream-read-0][method]descriptor.read-directory")]
    internal static extern int ReadEntries(int readable, nuint buffer, nuint count);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[stream-drop-readable-0][method]descriptor.read-directory")]
    internal static extern void DropReadEntries(int readable);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[async-lower][future-read-1][method]descriptor.read-directory")]
    internal static extern int ReadDirectoryResult(int readable, nuint result);

    [ComponentBuiltIn("wasi:filesystem/types@0.3.2", "[future-drop-readable-1][method]descriptor.read-directory")]
    internal static extern void DropDirectoryResult(int readable);
}
