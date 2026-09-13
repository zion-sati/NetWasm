namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class UtcLocalTimeOffsetResolver : ILocalTimeOffsetResolver
    {
        public LocalTimeOffset Resolve(long ticks, LocalTimeBasis basis) =>
            new(0, false);
    }
}
