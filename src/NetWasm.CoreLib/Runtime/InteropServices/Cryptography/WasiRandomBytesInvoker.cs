namespace System.Runtime.InteropServices.Cryptography
{
    internal sealed class WasiRandomBytesInvoker : IWasiRandomBytesInvoker
    {
        public void Invoke(long length, nuint result) =>
            WasiRandomBytesImports.GetRandomBytes(length, result);
    }
}
