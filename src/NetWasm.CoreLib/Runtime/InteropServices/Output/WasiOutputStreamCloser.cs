namespace System.Runtime.InteropServices.Output;

using System.Runtime.InteropServices.Concurrency;
using System.Runtime.InteropServices.WebAssembly;

// Ends the stream and waits until the host has written everything, as closing
// a desktop console stream flushes it. The host's result is not reported: a
// failed console write is not an error .NET surfaces either.
internal sealed class WasiOutputStreamCloser : IWasiOutputStreamCloser
{
    private const int Blocked = -1;

    private readonly IWasiByteStreamWritableDropInvoker _dropWritable;
    private readonly IWasiResultFutureReadInvoker _readResult;
    private readonly IWasiResultFutureDropInvoker _dropResult;
    private readonly IWaitableEventAwaiter _awaiter;
    private readonly ICanonicalBufferAllocator _allocator;

    internal WasiOutputStreamCloser(
        IWasiByteStreamWritableDropInvoker dropWritable,
        IWasiResultFutureReadInvoker readResult,
        IWasiResultFutureDropInvoker dropResult,
        IWaitableEventAwaiter awaiter,
        ICanonicalBufferAllocator allocator)
    {
        _dropWritable = dropWritable ?? throw new ArgumentNullException();
        _readResult = readResult ?? throw new ArgumentNullException();
        _dropResult = dropResult ?? throw new ArgumentNullException();
        _awaiter = awaiter ?? throw new ArgumentNullException();
        _allocator = allocator ?? throw new ArgumentNullException();
    }

    public void Close(WasiOutputStreamEnds ends)
    {
        _dropWritable.Drop(ends.Writable);
        // result<_, error-code>: a discriminant byte and an enum byte.
        using (var result = _allocator.Allocate(2, 1))
        {
            if (_readResult.Read(ends.Completion, result.Address) == Blocked)
            {
                _awaiter.Await(ends.Completion);
            }
        }

        _dropResult.Drop(ends.Completion);
    }
}
