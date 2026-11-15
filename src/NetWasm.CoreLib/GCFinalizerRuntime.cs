// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System;

internal static class GCFinalizerRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void WaitForPending();
}
