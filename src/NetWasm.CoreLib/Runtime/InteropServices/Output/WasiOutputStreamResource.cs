namespace System.Runtime.InteropServices.Output;

// One standard stream's write-via-stream, opened on first write and closed or
// ended once. The opened streams form a list that EndOpened ends when the
// process exits, as an operating system closes a process's descriptors.
internal sealed class WasiOutputStreamResource : IDisposable
{
    private static WasiOutputStreamResource? s_opened;

    private readonly IWasiOutputStreamOpener _opener;
    private readonly IWasiOutputStreamCloser _closer;
    private readonly IWasiByteStreamWritableDropInvoker _dropWritable;
    private WasiOutputStreamEnds _ends;
    private bool _opened;
    private bool _disposed;
    private WasiOutputStreamResource? _nextOpened;

    internal WasiOutputStreamResource(
        IWasiOutputStreamOpener opener,
        IWasiOutputStreamCloser closer,
        IWasiByteStreamWritableDropInvoker dropWritable)
    {
        _opener = opener ?? throw new ArgumentNullException();
        _closer = closer ?? throw new ArgumentNullException();
        _dropWritable = dropWritable ?? throw new ArgumentNullException();
    }

    internal int Writable
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }

            if (!_opened)
            {
                _ends = _opener.Open();
                _opened = true;
                _nextOpened = s_opened;
                s_opened = this;
            }

            return _ends.Writable;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_opened)
        {
            _closer.Close(_ends);
        }
    }

    // A process entry calls this after Main returns.
    internal static void EndOpened()
    {
        var resource = s_opened;
        s_opened = null;
        while (resource is not null)
        {
            resource.End();
            resource = resource._nextOpened;
        }
    }

    // Ends an open stream at process exit, unless it was already closed: dropping
    // the writable end tells the host the output is complete. The completion
    // future goes with the exiting instance.
    internal void End()
    {
        if (_disposed || !_opened)
        {
            return;
        }

        _disposed = true;
        _dropWritable.Drop(_ends.Writable);
    }
}
