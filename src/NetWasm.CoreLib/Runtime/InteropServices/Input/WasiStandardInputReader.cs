namespace System.Runtime.InteropServices.Input;

using System.IO;
using System.Runtime.InteropServices.Concurrency;
using System.Runtime.InteropServices.Output;
using System.Runtime.InteropServices.Streams;
using System.Runtime.InteropServices.WebAssembly;

// Reads standard input through wasi:cli/stdin's read-via-stream, opened on the first read and
// kept for the rest of the program. Each read is an async-lowered copy, waiting while it is
// blocked. When the host ends the stream, its completion future says whether input ended or
// failed; a failure is an IOException from then on, after any bytes already copied.
internal sealed class WasiStandardInputReader : IStandardInputReader
{
    private const int Blocked = -1;
    private const uint Completed = 0;
    private const uint Dropped = 1;

    private readonly IWasiStandardInputOpenInvoker _open;
    private readonly IWasiByteStreamReadInvoker _read;
    private readonly IWasiByteStreamReadableDropInvoker _dropReadable;
    private readonly IWasiResultFutureReadInvoker _readResult;
    private readonly IWasiResultFutureDropInvoker _dropResult;
    private readonly IWaitableEventAwaiter _awaiter;
    private readonly ICanonicalBufferAllocator _allocator;
    private WasiReadableStreamEnds _ends;
    private bool _opened;
    private bool _ended;
    private string? _failure;

    internal WasiStandardInputReader(
        IWasiStandardInputOpenInvoker open,
        IWasiByteStreamReadInvoker read,
        IWasiByteStreamReadableDropInvoker dropReadable,
        IWasiResultFutureReadInvoker readResult,
        IWasiResultFutureDropInvoker dropResult,
        IWaitableEventAwaiter awaiter,
        ICanonicalBufferAllocator allocator)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _dropReadable = dropReadable ?? throw new ArgumentNullException(nameof(dropReadable));
        _readResult = readResult ?? throw new ArgumentNullException(nameof(readResult));
        _dropResult = dropResult ?? throw new ArgumentNullException(nameof(dropResult));
        _awaiter = awaiter ?? throw new ArgumentNullException(nameof(awaiter));
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
    }

    public int Read(Span<byte> destination)
    {
        if (_failure is not null)
        {
            throw new IOException(_failure);
        }

        if (destination.IsEmpty || _ended)
        {
            return 0;
        }

        if (!_opened)
        {
            _ends = _open.Open();
            _opened = true;
        }

        using var buffer = _allocator.Allocate(unchecked((nuint)destination.Length), 1);
        while (true)
        {
            var status = _read.Read(_ends.Readable, buffer.Address, unchecked((nuint)destination.Length));
            var progress = status == Blocked ? _awaiter.Await(_ends.Readable) : unchecked((uint)status);
            var count = unchecked((int)(progress >> 4));
            for (var index = 0; index < count; index++)
            {
                destination[index] = CanonicalAbi.ReadByte(buffer.Address, unchecked((nuint)index));
            }

            var copy = progress & 0xF;
            if (copy == Dropped)
            {
                End();
            }
            else if (copy != Completed)
            {
                throw new IOException("Standard input was cancelled, which NetWasm never asks for.");
            }

            if (count != 0)
            {
                return count;
            }

            if (_ended)
            {
                return _failure is null ? 0 : throw new IOException(_failure);
            }
        }
    }

    // The host ended the stream: read its completion, result<_, error-code>, whose error-code
    // is an enum (io, illegal-byte-sequence, pipe) in the byte after the discriminant.
    private void End()
    {
        _ended = true;
        _dropReadable.Drop(_ends.Readable);
        using var result = _allocator.Allocate(2, 1);
        if (_readResult.Read(_ends.Completion, result.Address) == Blocked)
        {
            _awaiter.Await(_ends.Completion);
        }

        _dropResult.Drop(_ends.Completion);
        if (CanonicalAbi.ReadByte(result.Address, 0) != 0)
        {
            _failure = "Standard input could not be read (" +
                CanonicalAbi.ReadByte(result.Address, 1) switch
                {
                    0 => "io",
                    1 => "illegal-byte-sequence",
                    2 => "pipe",
                    var code => "error-code " + code,
                } + ").";
        }
    }
}
