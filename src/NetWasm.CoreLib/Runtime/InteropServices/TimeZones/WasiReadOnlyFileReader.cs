namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Streams;
    using System.Runtime.InteropServices.Concurrency;
    using System.Runtime.InteropServices.Output;
    using System.Runtime.InteropServices.WebAssembly;

    // Reads through read-via-stream with async-lowered copies, waiting when a
    // copy is blocked. The host ends the stream at the end of the file and
    // reports a read failure through the completion future.
    internal sealed class WasiReadOnlyFileReader : IReadOnlyFileReader
    {
        private const int Blocked = -1;
        private const uint Completed = 0;
        private const uint Dropped = 1;

        private readonly IWasiFileStreamOpenInvoker _open;
        private readonly IWasiByteStreamReadInvoker _read;
        private readonly IWasiByteStreamReadableDropInvoker _dropReadable;
        private readonly IWasiResultFutureReadInvoker _readResult;
        private readonly IWasiResultFutureDropInvoker _dropResult;
        private readonly IWaitableEventAwaiter _awaiter;
        private readonly ICanonicalBufferAllocator _allocator;

        internal WasiReadOnlyFileReader(
            IWasiFileStreamOpenInvoker open,
            IWasiByteStreamReadInvoker read,
            IWasiByteStreamReadableDropInvoker dropReadable,
            IWasiResultFutureReadInvoker readResult,
            IWasiResultFutureDropInvoker dropResult,
            IWaitableEventAwaiter awaiter,
            ICanonicalBufferAllocator allocator)
        {
            _open = open ?? throw new ArgumentNullException();
            _read = read ?? throw new ArgumentNullException();
            _dropReadable = dropReadable ?? throw new ArgumentNullException();
            _readResult = readResult ?? throw new ArgumentNullException();
            _dropResult = dropResult ?? throw new ArgumentNullException();
            _awaiter = awaiter ?? throw new ArgumentNullException();
            _allocator = allocator ?? throw new ArgumentNullException();
        }

        // Returns fewer than length bytes only at the end of the file.
        public byte[] Read(int handle, long offset, int length)
        {
            if (offset < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (length == 0)
            {
                return [];
            }

            var ends = _open.Open(handle, offset);
            using var buffer = _allocator.Allocate(unchecked((nuint)length), 1);
            var count = 0;
            var ended = false;
            while (count < length)
            {
                var status = _read.Read(
                    ends.Readable,
                    buffer.Address + unchecked((nuint)count),
                    unchecked((nuint)(length - count)));
                var progress = status == Blocked
                    ? _awaiter.Await(ends.Readable)
                    : unchecked((uint)status);
                count += unchecked((int)(progress >> 4));
                var copy = progress & 0xF;
                if (copy == Dropped)
                {
                    ended = true;
                    break;
                }

                if (copy != Completed)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem cancelled a timezone asset read.");
                }
            }

            _dropReadable.Drop(ends.Readable);
            if (ended)
            {
                ThrowOnFailure(ends.Completion);
            }

            _dropResult.Drop(ends.Completion);
            var contents = new byte[count];
            for (var index = 0; index < count; index++)
            {
                contents[index] = CanonicalAbi.ReadByte(buffer.Address, unchecked((nuint)index));
            }

            return contents;
        }

        // result<_, error-code>: a discriminant byte, then at offset 4 the error-code
        // variant's discriminant (its other(option<string>) case makes it 20 bytes).
        private void ThrowOnFailure(int completion)
        {
            using var result = _allocator.Allocate(WasiReadOnlyFileOpener.ErrorCodeResultSize, 4);
            if (_readResult.Read(completion, result.Address) == Blocked)
            {
                _awaiter.Await(completion);
            }

            if (CanonicalAbi.ReadByte(result.Address, 0) != 0)
            {
                _dropResult.Drop(completion);
                throw new PlatformNotSupportedException(
                    "WASI filesystem could not read the timezone asset (error-code=" +
                    CanonicalAbi.ReadByte(result.Address, 4) + ").");
            }
        }
    }
}
