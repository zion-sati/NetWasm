// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetEntry
    {
        internal TimeZoneAssetEntry(
            string name,
            int initialOffsetSeconds,
            bool initialDaylightSavingTime,
            int transitionCount,
            long transitionTableOffset)
        {
            Name = name ?? throw new ArgumentNullException();
            if (transitionCount < 0 || transitionTableOffset < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            InitialOffsetSeconds = initialOffsetSeconds;
            InitialDaylightSavingTime = initialDaylightSavingTime;
            TransitionCount = transitionCount;
            TransitionTableOffset = transitionTableOffset;
        }

        internal string Name { get; }
        internal int InitialOffsetSeconds { get; }
        internal bool InitialDaylightSavingTime { get; }
        internal int TransitionCount { get; }
        internal long TransitionTableOffset { get; }
    }
}
