// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiSubtaskCancelInvoker : IWasiSubtaskCancelInvoker
{
    public int Cancel(int subtask) => WaitableSetImports.CancelSubtask(subtask);
}
