namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneAssetByteReader
    {
        byte[] Read(string assetName);
    }
}
