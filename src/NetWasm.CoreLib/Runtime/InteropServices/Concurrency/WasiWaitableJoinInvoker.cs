// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiWaitableJoinInvoker : IWasiWaitableJoinInvoker
{
    public void Join(int waitable, int set) => WaitableSetImports.Join(waitable, set);
}
