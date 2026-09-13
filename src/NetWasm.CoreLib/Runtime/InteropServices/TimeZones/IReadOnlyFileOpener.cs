namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IReadOnlyFileOpener
    {
        int Open(int directoryHandle, string relativePath);
    }
}
