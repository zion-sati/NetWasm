namespace System.Runtime.InteropServices.Cryptography
{
    internal interface ICryptographicallySecureRandomByteFiller
    {
        void Fill(nuint destination, int length);
    }
}
