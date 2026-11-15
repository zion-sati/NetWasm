// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiOpenAtInvoker : IWasiOpenAtInvoker
    {
        public int Invoke(nuint parameters, nuint result) =>
            WasiTimeZoneImports.OpenAt(parameters, result);
    }
}
