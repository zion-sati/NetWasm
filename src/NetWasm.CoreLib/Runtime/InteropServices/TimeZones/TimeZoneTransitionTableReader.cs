// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneTransitionTableReader : ITimeZoneTransitionTableReader
    {
        public TimeZoneTransition[] Read(TimeZoneAsset asset, TimeZoneAssetEntry zone)
        {
            if (asset == null || zone == null)
            {
                throw new ArgumentNullException();
            }
            if (zone.TransitionCount == 0)
            {
                return Array.Empty<TimeZoneTransition>();
            }
            if (zone.TransitionCount > int.MaxValue / TimeZoneAssetCursor.TransitionLength)
            {
                throw TimeZoneAssetCursor.Invalid("transition table is truncated");
            }
            var cursor = new TimeZoneAssetCursor(asset.File, zone.TransitionTableOffset);
            cursor.Fill(
                zone.TransitionCount * TimeZoneAssetCursor.TransitionLength,
                "transition table is truncated");
            var transitions = new TimeZoneTransition[zone.TransitionCount];
            var previousSeconds = long.MinValue;
            for (var index = 0; index < transitions.Length; index++)
            {
                var seconds = cursor.ReadInt64();
                if (seconds <= previousSeconds)
                {
                    throw TimeZoneAssetCursor.Invalid("transitions are not strictly ordered");
                }
                previousSeconds = seconds;
                transitions[index] = new TimeZoneTransition(
                    seconds,
                    cursor.ReadOffset(),
                    cursor.ReadBoolean());
            }
            return transitions;
        }
    }
}
