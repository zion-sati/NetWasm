// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System;

internal static class GCMetricRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern long Read(int metric);
}
