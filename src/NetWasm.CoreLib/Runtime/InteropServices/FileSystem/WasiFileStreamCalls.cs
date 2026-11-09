namespace System.Runtime.InteropServices.FileSystem;

using System.Collections.Generic;
using System.Runtime.InteropServices.Concurrency;
using System.Runtime.InteropServices.WebAssembly;
using Layout = WasiFileSystemLayout;

// The descriptor methods that move data through streams. A copy is async-lowered: BLOCKED
// (-1), or the count shifted left by 4 with the copy's result in the low 4 bits, and a blocked
// copy's event carries the same. When the host ends a stream early, its future reports why.

internal abstract class WasiStreamCall(IWaitableEventAwaiter awaiter, ICanonicalBufferAllocator allocator)
{
    private protected readonly IWaitableEventAwaiter Awaiter = awaiter ?? throw new ArgumentNullException(nameof(awaiter));
    private protected readonly ICanonicalBufferAllocator Allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));

    private protected uint Progress(int status, int waitable) =>
        status == Layout.Blocked ? Awaiter.Await(waitable) : unchecked((uint)status);

    // Waits for a future<result<_, error-code>> read that may be blocked.
    private protected FileSystemError Completion(int status, int future, CanonicalBuffer result)
    {
        if (status == Layout.Blocked)
        {
            Awaiter.Await(future);
        }

        return Layout.ReadResult(result.Address, Layout.Pointer);
    }

    // The two handles a stream-returning method stores: the readable end and its future.
    private protected static (int Readable, int Future) Ends(nuint result) =>
        (CanonicalAbi.ReadInt32(result, 0), CanonicalAbi.ReadInt32(result, 4));
}

// read-via-stream at the offset, one copy into the destination: a short read is fine, and 0
// bytes is the end of the file once the stream's future says it ended without error.
internal sealed class WasiFileReadCall(IWaitableEventAwaiter awaiter, ICanonicalBufferAllocator allocator) :
    WasiStreamCall(awaiter, allocator), IWasiFileReadCall
{
    public FileSystemError Read(int descriptor, long offset, Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (offset < 0)
        {
            return FileSystemError.InvalidArgument;
        }

        if (destination.IsEmpty)
        {
            return FileSystemError.None;
        }

        (int readable, int future) ends;
        using (var handles = Allocator.Allocate(8, 4))
        {
            WasiFileSystemImports.ReadViaStream(descriptor, offset, handles.Address);
            ends = Ends(handles.Address);
        }

        var length = unchecked((nuint)destination.Length);
        using var buffer = Allocator.Allocate(length, 1);
        uint copy;
        int count;
        do
        {
            var progress = Progress(WasiFileSystemImports.ReadBytes(ends.readable, buffer.Address, length), ends.readable);
            count = unchecked((int)(progress >> 4));
            copy = progress & 0xF;
        }
        while (count == 0 && copy == Layout.Completed);

        for (var index = 0; index < count; index++)
        {
            destination[index] = CanonicalAbi.ReadByte(buffer.Address, unchecked((nuint)index));
        }

        WasiFileSystemImports.DropReadBytes(ends.readable);
        var error = FileSystemError.None;
        if (count == 0)
        {
            error = copy == Layout.Dropped ? ReadCompletion(ends.future) : FileSystemError.IOError;
        }

        WasiFileSystemImports.DropReadResult(ends.future);
        bytesRead = count;
        return error;
    }

    private FileSystemError ReadCompletion(int future)
    {
        using var result = Allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        return Completion(WasiFileSystemImports.ReadReadResult(future, result.Address), future, result);
    }
}

// write-via-stream at the offset: every byte copied into the stream, which is then closed so
// the host finishes writing, and its future says whether it did.
internal sealed class WasiFileWriteCall(IWaitableEventAwaiter awaiter, ICanonicalBufferAllocator allocator) :
    WasiStreamCall(awaiter, allocator), IWasiFileWriteCall
{
    public FileSystemError Write(int descriptor, long offset, ReadOnlySpan<byte> source)
    {
        if (offset < 0)
        {
            return FileSystemError.InvalidArgument;
        }

        if (source.IsEmpty)
        {
            return FileSystemError.None;
        }

        var ends = WasiFileSystemImports.CreateWriteStream();
        var readable = unchecked((int)ends);
        var writable = unchecked((int)(ends >> 32));
        var future = WasiFileSystemImports.WriteViaStream(descriptor, readable, offset);
        var length = unchecked((nuint)source.Length);
        using var buffer = Allocator.Allocate(length, 1);
        for (var index = 0; index < source.Length; index++)
        {
            CanonicalAbi.WriteByte(buffer.Address, unchecked((nuint)index), source[index]);
        }

        nuint written = 0;
        var stopped = false;
        while (written < length)
        {
            var progress = Progress(
                WasiFileSystemImports.WriteBytes(writable, buffer.Address + written, length - written), writable);
            written += progress >> 4;
            if ((progress & 0xF) != Layout.Completed)
            {
                stopped = true;
                break;
            }
        }

        WasiFileSystemImports.DropWriteBytes(writable);
        FileSystemError error;
        using (var result = Allocator.Allocate(Layout.SmallResultSize, Layout.Pointer))
        {
            error = Completion(WasiFileSystemImports.ReadWriteResult(future, result.Address), future, result);
        }

        WasiFileSystemImports.DropWriteResult(future);
        return error == FileSystemError.None && stopped ? FileSystemError.IOError : error;
    }
}

// read-directory: its stream<directory-entry> read to the end, a batch of entries per copy, then
// its future for whether the host listed the whole directory.
internal sealed class WasiDirectoryReadCall(IWaitableEventAwaiter awaiter, ICanonicalBufferAllocator allocator) :
    WasiStreamCall(awaiter, allocator), IWasiDirectoryReadCall
{
    private const int Batch = 64;

    public FileSystemError Read(int descriptor, out DirectoryEntry[] entries)
    {
        (int readable, int future) ends;
        using (var handles = Allocator.Allocate(8, 4))
        {
            WasiFileSystemImports.ReadDirectory(descriptor, handles.Address);
            ends = Ends(handles.Address);
        }

        var size = Layout.DirectoryEntrySize;
        var listed = new List<DirectoryEntry>();
        var failed = false;
        using (var buffer = Allocator.Allocate(size * Batch, Layout.Pointer))
        {
            while (true)
            {
                var progress = Progress(
                    WasiFileSystemImports.ReadEntries(ends.readable, buffer.Address, Batch), ends.readable);
                var count = unchecked((int)(progress >> 4));
                for (var index = 0; index < count; index++)
                {
                    var entry = buffer.Address + size * unchecked((nuint)index);
                    var kind = Layout.ReadKind(entry);
                    listed.Add(new DirectoryEntry(Layout.TakeString(entry + Layout.Pointer * 4), kind));
                }

                var copy = progress & 0xF;
                if (copy == Layout.Dropped)
                {
                    break;
                }

                if (copy != Layout.Completed)
                {
                    failed = true;
                    break;
                }
            }
        }

        WasiFileSystemImports.DropReadEntries(ends.readable);
        FileSystemError error;
        using (var result = Allocator.Allocate(Layout.SmallResultSize, Layout.Pointer))
        {
            error = Completion(WasiFileSystemImports.ReadDirectoryResult(ends.future, result.Address), ends.future, result);
        }

        WasiFileSystemImports.DropDirectoryResult(ends.future);
        if (error == FileSystemError.None && failed)
        {
            error = FileSystemError.IOError;
        }

        entries = error == FileSystemError.None ? listed.ToArray() : [];
        return error;
    }
}
