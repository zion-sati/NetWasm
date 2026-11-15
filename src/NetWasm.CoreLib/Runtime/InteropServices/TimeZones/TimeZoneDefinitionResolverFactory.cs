// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneDefinitionResolverFactory(
        ITimeZoneTransitionTableReader transitions) :
        ITimeZoneDefinitionResolverFactory
    {
        private readonly ITimeZoneTransitionTableReader _transitions =
            transitions ?? throw new ArgumentNullException();

        public ITimeZoneDefinitionResolver Create(TimeZoneAsset asset) =>
            new TimeZoneDefinitionResolver(asset, _transitions);
    }
}
