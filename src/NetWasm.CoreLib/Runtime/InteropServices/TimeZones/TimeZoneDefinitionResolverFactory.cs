namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneDefinitionResolverFactory :
        ITimeZoneDefinitionResolverFactory
    {
        public ITimeZoneDefinitionResolver Create(TimeZoneAsset asset) =>
            new TimeZoneDefinitionResolver(asset);
    }
}
