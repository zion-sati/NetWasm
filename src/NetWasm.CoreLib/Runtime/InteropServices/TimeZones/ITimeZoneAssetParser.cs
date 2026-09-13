namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneAssetParser
    {
        TimeZoneAsset Parse(byte[] contents);
    }
}
