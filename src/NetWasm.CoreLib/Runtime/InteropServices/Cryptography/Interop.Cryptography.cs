namespace System
{
    using Runtime.InteropServices.Cryptography;

    internal static unsafe partial class Interop
    {
        internal static void GetCryptographicallySecureRandomBytes(
            byte* buffer,
            int length) => Runtime.InteropServices.PlatformServices.Random.Fill(
                new Span<byte>(buffer, length));
    }
}
