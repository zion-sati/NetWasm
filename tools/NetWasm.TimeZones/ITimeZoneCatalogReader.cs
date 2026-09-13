namespace NetWasm.TimeZones;

internal interface ITimeZoneCatalogReader
{
    TimeZoneCatalog Read(string sourceDirectory);
}
