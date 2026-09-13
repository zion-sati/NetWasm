namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class AssetLocalTimeOffsetResolver : ILocalTimeOffsetResolver
    {
        private readonly TimeZoneDefinition _zone;
        private readonly ITimeZoneTransitionResolver _transitions;

        internal AssetLocalTimeOffsetResolver(
            TimeZoneDefinition zone,
            ITimeZoneTransitionResolver transitions)
        {
            _zone = zone ?? throw new ArgumentNullException();
            _transitions = transitions ?? throw new ArgumentNullException();
        }

        public LocalTimeOffset Resolve(long ticks, LocalTimeBasis basis) =>
            _transitions.Resolve(_zone, ticks, basis);
    }
}
