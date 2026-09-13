namespace System
{
    using Runtime.InteropServices.Cryptography;

    internal static unsafe class Interop
    {
        internal static void GetCryptographicallySecureRandomBytes(
            byte* buffer,
            int length) => CryptographicRandomServices.Filler.Fill(
                unchecked((nuint)buffer),
                length);
    }
}
