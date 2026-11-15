// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    using System.Collections.Generic;

    internal sealed class TimeZoneDefinitionResolver : ITimeZoneDefinitionResolver
    {
        private readonly TimeZoneAsset _asset;
        private readonly ITimeZoneTransitionTableReader _transitions;
        private readonly Dictionary<string, TimeZoneAssetEntry> _zones;
        private readonly Dictionary<string, TimeZoneDefinition> _definitions =
            new Dictionary<string, TimeZoneDefinition>(StringComparer.Ordinal);

        internal TimeZoneDefinitionResolver(
            TimeZoneAsset asset,
            ITimeZoneTransitionTableReader transitions)
        {
            _asset = asset ?? throw new ArgumentNullException();
            _transitions = transitions ?? throw new ArgumentNullException();
            _zones = new Dictionary<string, TimeZoneAssetEntry>(
                asset.Zones.Length,
                StringComparer.Ordinal);
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
            if (_definitions.TryGetValue(name, out var definition))
            {
                return definition;
            }
            if (!_zones.TryGetValue(name, out var zone))
            {
                throw new PlatformNotSupportedException(
                    "Timezone asset does not contain TZ='" + name + "'.");
            }
            definition = new TimeZoneDefinition(
                zone.Name,
                zone.InitialOffsetSeconds,
                zone.InitialDaylightSavingTime,
                _transitions.Read(_asset, zone));
            _definitions.Add(name, definition);
            return definition;
        }
    }
}
