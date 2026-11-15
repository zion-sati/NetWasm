// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class LocalTimeOffsetResolverInstaller :
        ILocalTimeOffsetResolverInstaller
    {
        public void Install(ILocalTimeOffsetResolver resolver) =>
            PlatformServices.UseLocalTimeOffsetResolver(resolver);
    }
}
