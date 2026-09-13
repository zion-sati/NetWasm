namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneDefinitionResolverFactory
    {
        ITimeZoneDefinitionResolver Create(TimeZoneAsset asset);
    }
}
