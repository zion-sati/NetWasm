namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ILocalTimeOffsetResolverFactory
    {
        ILocalTimeOffsetResolver Create(TimeZoneDefinition zone);
    }
}
