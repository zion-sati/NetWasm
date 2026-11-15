// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneCatalogSnapshot
    {
        internal TimeZoneCatalogSnapshot(
            string? localId,
            TimeZoneAsset? asset,
            ITimeZoneDefinitionResolver? definitions)
        {
            if ((asset == null) != (definitions == null))
            {
                throw new ArgumentException();
            }

            LocalId = localId;
            Asset = asset;
            Definitions = definitions;
        }

        internal string? LocalId { get; }

        internal TimeZoneAsset? Asset { get; }

        internal ITimeZoneDefinitionResolver? Definitions { get; }
    }
}
