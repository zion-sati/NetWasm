namespace System.Runtime.InteropServices.Cryptography
{
    internal interface IWasiRandomBytesInvoker
    {
        void Invoke(long length, nuint result);
    }
}
