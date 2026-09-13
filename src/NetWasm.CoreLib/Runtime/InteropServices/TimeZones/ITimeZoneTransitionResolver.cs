namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneTransitionResolver
    {
        LocalTimeOffset Resolve(
            TimeZoneDefinition zone,
            long ticks,
            LocalTimeBasis basis);
    }
}
