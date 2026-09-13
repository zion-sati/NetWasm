namespace System.Runtime.InteropServices.Cryptography
{
    internal static class CryptographicRandomServices
    {
        private static ICryptographicallySecureRandomByteFiller? s_filler;

        internal static ICryptographicallySecureRandomByteFiller Filler =>
            s_filler ??= new WasiCryptographicallySecureRandomByteFiller(
                new WasiRandomBytesInvoker());
    }
}
