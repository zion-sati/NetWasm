namespace System.Runtime.InteropServices.Cryptography
{
    using System.Runtime.InteropServices.WebAssembly;
    using System.Security.Cryptography;

    internal sealed class WasiCryptographicallySecureRandomByteFiller(
        IWasiRandomBytesInvoker invoker) : ICryptographicallySecureRandomByteFiller
    {
        private readonly IWasiRandomBytesInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public void Fill(nuint destination, int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if (length != 0 && destination == 0)
            {
                throw new ArgumentNullException(nameof(destination));
            }

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
                        CanonicalAbi.WriteByte(
                            destination,
                            unchecked((nuint)index),
                            CanonicalAbi.ReadByte(bytes, unchecked((nuint)index)));
                    }
                }
                finally
                {
                    CanonicalAbi.Free(bytes);
                }
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }
}
