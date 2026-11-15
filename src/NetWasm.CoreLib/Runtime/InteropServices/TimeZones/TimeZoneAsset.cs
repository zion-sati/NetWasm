// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAsset
    {
        internal TimeZoneAsset(
            string dataVersion,
            string identity,
            TimeZoneAssetEntry[] zones,
            ITimeZoneAssetFile file)
        {
            DataVersion = dataVersion ?? throw new ArgumentNullException();
            Identity = identity ?? throw new ArgumentNullException();
            Zones = zones ?? throw new ArgumentNullException();
            File = file ?? throw new ArgumentNullException();
        }

        internal string DataVersion { get; }
        internal string Identity { get; }
        internal TimeZoneAssetEntry[] Zones { get; }
        internal ITimeZoneAssetFile File { get; }
    }
}
