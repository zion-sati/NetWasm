namespace NetWasm.TimeZones;

internal interface ITimeZoneBrowserLoaderEncoder
{
    byte[] Encode(string assetFile);
}
