namespace System.Runtime.InteropServices.TimeZones
{
    using System.Collections.Generic;

    internal sealed class TimeZoneDefinitionResolver : ITimeZoneDefinitionResolver
    {
        private readonly Dictionary<string, TimeZoneDefinition> _zones;

        internal TimeZoneDefinitionResolver(TimeZoneAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException();
            }
            _zones = new Dictionary<string, TimeZoneDefinition>(asset.Zones.Length);
            foreach (var zone in asset.Zones)
            {
                _zones.Add(zone.Name, zone);
            }
        }

        public TimeZoneDefinition Resolve(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException();
            }
            if (!_zones.TryGetValue(name, out var zone))
            {
                throw new PlatformNotSupportedException(
                    "Timezone asset does not contain TZ='" + name + "'.");
            }
            return zone;
        }
    }
}
