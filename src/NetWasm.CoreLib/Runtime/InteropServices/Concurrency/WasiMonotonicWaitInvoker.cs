// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

// wasi:clocks/monotonic-clock: its wait-for, async-lowered, and its reading, the deadlines' clock.
internal sealed class WasiMonotonicWaitInvoker : IWasiMonotonicWaitInvoker, IWasiMonotonicNowInvoker
{
    public int WaitFor(ulong nanoseconds) => WaitableSetImports.WaitFor(unchecked((long)nanoseconds));

    public ulong Now() => unchecked((ulong)WasiClockImports.GetMonotonicTime());
}
