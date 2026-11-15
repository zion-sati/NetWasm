// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal interface ISubtaskReturnAwaiter
{
    // Finishes an async-lowered call from the status it returned: waits until
    // the callee has returned, then releases the subtask.
    void AwaitReturn(int status);
}
