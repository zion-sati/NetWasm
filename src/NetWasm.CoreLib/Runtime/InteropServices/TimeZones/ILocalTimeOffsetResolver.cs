namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ILocalTimeOffsetResolver
    {
        LocalTimeOffset Resolve(long ticks, LocalTimeBasis basis);
    }
}
