namespace System.Runtime.InteropServices.Streams;

// The guest's ends of a WASI function that hands it a stream<u8> to read, such as
// read-via-stream: the stream, and the future<result<_, error-code>> that reports whether the
// host produced it without error.
internal readonly struct WasiReadableStreamEnds
{
    internal WasiReadableStreamEnds(int readable, int completion)
    {
        if (readable == 0 || completion == 0)
        {
            throw new ArgumentOutOfRangeException();
        }

        Readable = readable;
        Completion = completion;
    }

    internal int Readable { get; }

    internal int Completion { get; }
}
