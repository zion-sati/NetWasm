// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiThreadYieldInvoker
{
    // Lets the host and other tasks run before the current task continues; where the task cannot
    // block, returns at once.
    void Yield();
}
