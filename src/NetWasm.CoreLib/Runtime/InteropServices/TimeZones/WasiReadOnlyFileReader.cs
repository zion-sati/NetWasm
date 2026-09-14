namespace System.Runtime.InteropServices.TimeZones
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class WasiReadOnlyFileReader(
        IWasiDescriptorReadInvoker invoker) : IReadOnlyFileReader
    {
        private const long ReadChunkLength = 64 * 1024;
        private readonly IWasiDescriptorReadInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public byte[] Read(int handle)
        {
            var contents = new List<byte>();
            long offset = 0;
            while (true)
            {
                var chunk = ReadChunk(handle, offset);
                if (chunk.Contents.Length == 0 && !chunk.EndOfFile)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem made no progress while reading the timezone asset.");
                }
                contents.AddRange(chunk.Contents);
                offset = checked(offset + chunk.Contents.Length);
                if (chunk.EndOfFile)
                {
                    return contents.ToArray();
                }
            }
        }

        private ReadChunkResult ReadChunk(int handle, long offset)
        {
            var addressSize = unchecked((nuint)nuint.Size);
            var payloadOffset = addressSize;
            var result = CanonicalAbi.Allocate(addressSize * 3 + 1, addressSize);
            try
            {
                _invoker.Invoke(handle, ReadChunkLength, offset, result);
                var tag = CanonicalAbi.ReadByte(result, 0);
                if (tag == 1)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem could not read the timezone asset (error-code=" +
                        CanonicalAbi.ReadInt32(result, payloadOffset) + ").");
                }
                if (tag != 0)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem returned an invalid read result.");
                }
                var bytesAddress = CanonicalAbi.ReadAddress(result, payloadOffset);
                var bytesLength = CanonicalAbi.ReadAddress(result, payloadOffset + addressSize);
                if (bytesLength > int.MaxValue)
                {
                    throw new OutOfMemoryException();
                }
                var bytes = new byte[(int)bytesLength];
                try
                {
                    for (var index = 0; index < bytes.Length; index++)
                    {
                        bytes[index] = CanonicalAbi.ReadByte(
                            bytesAddress,
                            unchecked((nuint)index));
                    }
                }
                finally
                {
                    if (bytesLength != 0)
                    {
                        CanonicalAbi.Free(bytesAddress);
                    }
                }
                return new ReadChunkResult(
                    bytes,
                    ReadBoolean(result, payloadOffset + addressSize * 2));
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }

        private static bool ReadBoolean(nuint address, nuint offset) =>
            CanonicalAbi.ReadByte(address, offset) switch
            {
                0 => false,
                1 => true,
                _ => throw new PlatformNotSupportedException(
                    "WASI filesystem returned an invalid end-of-file flag."),
            };

        private readonly struct ReadChunkResult
        {
            internal ReadChunkResult(byte[] contents, bool endOfFile)
            {
                Contents = contents;
                EndOfFile = endOfFile;
            }

            internal byte[] Contents { get; }
            internal bool EndOfFile { get; }
        }
    }
}
