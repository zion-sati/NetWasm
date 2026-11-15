// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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
