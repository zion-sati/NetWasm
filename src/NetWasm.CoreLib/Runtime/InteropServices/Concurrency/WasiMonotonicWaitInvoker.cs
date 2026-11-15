// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiMonotonicWaitInvoker : IWasiMonotonicWaitInvoker
{
    public int WaitFor(ulong nanoseconds) => WaitableSetImports.WaitFor(unchecked((long)nanoseconds));
}
