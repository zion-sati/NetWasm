namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneDefinitionResolver
    {
        TimeZoneDefinition Resolve(string name);
    }
}
