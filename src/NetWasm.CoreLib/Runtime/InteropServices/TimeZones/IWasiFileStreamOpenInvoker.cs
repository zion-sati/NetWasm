// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Streams;

    internal interface IWasiFileStreamOpenInvoker
    {
        WasiReadableStreamEnds Open(int handle, long offset);
    }
}
