namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IReadOnlyFileReader
    {
        byte[] Read(int handle);
    }
}
