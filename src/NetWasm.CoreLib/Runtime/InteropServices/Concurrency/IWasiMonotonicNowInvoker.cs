// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiMonotonicNowInvoker
{
    // The monotonic clock's reading, in nanoseconds.
    ulong Now();
}
