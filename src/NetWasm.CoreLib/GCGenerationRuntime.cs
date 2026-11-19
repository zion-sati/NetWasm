// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System;

internal static class GCGenerationRuntime
{
    // The collector's generation of the object.
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int Of(object value);
}
