// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetValidationException :
        PlatformNotSupportedException
    {
        internal TimeZoneAssetValidationException(string reason) :
            base("Timezone asset validation failed: " + reason + ".")
        {
            Reason = reason ?? throw new ArgumentNullException();
        }

        internal string Reason { get; }
    }
}
