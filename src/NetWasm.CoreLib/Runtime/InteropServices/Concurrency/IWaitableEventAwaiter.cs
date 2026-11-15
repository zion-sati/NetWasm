// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

internal interface IWaitableEventAwaiter
{
    // Blocks until the waitable has an event and returns the event's payload.
    uint Await(int waitable);
}
