// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Input;

using System.Runtime.InteropServices.Streams;

internal interface IWasiStandardInputOpenInvoker
{
    // Calls read-via-stream and returns the guest's ends.
    WasiReadableStreamEnds Open();
}
