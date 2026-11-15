// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class UtcLocalTimeOffsetResolver : ILocalTimeOffsetResolver
    {
        public LocalTimeOffset Resolve(long ticks, LocalTimeBasis basis) =>
            new(0, false);
    }
}
