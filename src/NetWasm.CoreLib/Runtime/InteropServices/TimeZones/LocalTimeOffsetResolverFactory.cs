namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class LocalTimeOffsetResolverFactory(
        ITimeZoneTransitionResolver transitions) : ILocalTimeOffsetResolverFactory
    {
        private readonly ITimeZoneTransitionResolver _transitions =
            transitions ?? throw new ArgumentNullException();

        public ILocalTimeOffsetResolver Create(TimeZoneDefinition zone) =>
            new AssetLocalTimeOffsetResolver(zone, _transitions);
    }
}
