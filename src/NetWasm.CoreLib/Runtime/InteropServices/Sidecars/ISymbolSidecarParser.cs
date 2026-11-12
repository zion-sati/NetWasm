namespace System.Runtime.InteropServices.Sidecars
{
    internal interface ISymbolSidecarParser
    {
        // The file as a sidecar of that kind from that build; throws FormatException naming what is wrong.
        SymbolSidecar Parse(byte[] bytes, ushort kind, string buildId);
    }
}
