namespace System.Runtime.InteropServices.Cryptography
{
    using System.Runtime.InteropServices.WebAssembly;
    using System.Security.Cryptography;

    internal sealed class WasiCryptographicallySecureRandomByteFiller(
        IWasiRandomBytesInvoker invoker) : IRandomSource
    {
        private readonly IWasiRandomBytesInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public void Fill(Span<byte> destination)
        {
            var length = destination.Length;
            var addressSize = unchecked((nuint)nuint.Size);
            var result = CanonicalAbi.Allocate(addressSize * 2, addressSize);
            try
            {
                _invoker.Invoke(length, result);
                var bytes = CanonicalAbi.ReadAddress(result, 0);
                var byteCount = CanonicalAbi.ReadAddress(result, addressSize);
                try
                {
                    if (byteCount != unchecked((nuint)length) ||
                        byteCount != 0 && bytes == 0)
                    {
                        throw new CryptographicException(
                            "WASI random returned an invalid byte sequence.");
                    }
                    for (var index = 0; index < length; index++)
                    {
                        destination[index] = CanonicalAbi.ReadByte(bytes, unchecked((nuint)index));
                    }
                }
                finally
                {
                    if (byteCount != 0)
                    {
                        CanonicalAbi.Free(bytes);
                    }
                }
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }
}
