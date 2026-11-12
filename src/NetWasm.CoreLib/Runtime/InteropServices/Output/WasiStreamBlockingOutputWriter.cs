namespace System.Runtime.InteropServices.Output;

using System.Runtime.InteropServices.Concurrency;

// Writes every byte before returning. Each copy is async-lowered, because
// synchronous stream built-ins are outside WASI 0.3's Component Model feature
// set; a blocked copy waits for its event. The host reads the array in place:
// no managed code runs, so nothing moves it, until the copy completes. A copy
// that completes at once still leaves the host to pass the bytes on, which it
// does only when it next runs, so the writer then yields to it: output appears
// as the program writes it, as on desktop .NET, even when the program never
// waits again (issue 224).
// Either standard stream's writer: the stream is the one its bindings open. The stream stays open
// until the process exits (WasiOutputStreamResource.EndOpened).
internal sealed unsafe class WasiStreamBlockingOutputWriter :
    WebAssembly.IStandardOutputWriter,
    WebAssembly.IStandardErrorWriter
{
    private const int Blocked = -1;
    private const uint Completed = 0;
    private const uint Dropped = 1;

    private readonly WasiOutputStreamResource _resource;
    private readonly IWasiByteStreamWriteInvoker _write;
    private readonly IWaitableEventAwaiter _awaiter;
    private readonly IWasiThreadYieldInvoker _yield;
    private bool _readerGone;

    internal WasiStreamBlockingOutputWriter(
        WasiOutputStreamResource resource,
        IWasiByteStreamWriteInvoker write,
        IWaitableEventAwaiter awaiter,
        IWasiThreadYieldInvoker yield)
    {
        _resource = resource ?? throw new ArgumentNullException();
        _write = write ?? throw new ArgumentNullException();
        _awaiter = awaiter ?? throw new ArgumentNullException();
        _yield = yield ?? throw new ArgumentNullException();
    }

    public void Write(ReadOnlySpan<byte> contents)
    {
        // A reader that has gone away is a closed pipe, which .NET's console
        // ignores rather than reports.
        if (contents.Length == 0 || _readerGone)
        {
            return;
        }

        var writable = _resource.Writable;
        fixed (byte* address = contents)
        {
            Copy(writable, (nuint)address, unchecked((uint)contents.Length));
        }
    }

    private void Copy(int writable, nuint address, uint length)
    {
        var written = 0u;
        var waited = false;
        while (written < length)
        {
            var status = _write.Write(
                writable,
                address + written,
                length - written);
            waited = status == Blocked;
            var progress = waited
                ? _awaiter.Await(writable)
                : unchecked((uint)status);
            written += progress >> 4;
            var result = progress & 0xF;
            if (result == Dropped)
            {
                _readerGone = true;
                return;
            }

            if (result != Completed)
            {
                throw new InvalidOperationException();
            }
        }

        // A wait for the last copy already let the host run.
        if (!waited)
        {
            _yield.Yield();
        }
    }
}
