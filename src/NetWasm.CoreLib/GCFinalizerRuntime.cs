// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace System;

internal static class GCFinalizerRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void WaitForPending();

    // Runs the finalizers a collection has queued, as Mono's single-threaded runtime runs them from its event loop: the
    // command world's event loop calls it every few dozen work items, so a program that keeps working without returning
    // to its host still finalizes, and frees, what it drops. The loop runs only inside the host's calls into the
    // program, with no managed caller to catch what it throws, so an exception a finalizer lets escape ends the
    // program, as one on .NET's finalizer thread does. A program with no finalizable type compiles it to nothing.
    internal static void RunQueuedBetweenWork()
    {
        if (RunQueued() is { } escaped)
        {
            ExceptionDispatchInfo.Throw(escaped);
        }
    }

    // The runtime's finalizer safepoint: the exception a finalizer let escape, or null.
    [MethodImpl(MethodImplOptions.InternalCall)]
    private static extern Exception? RunQueued();
}
