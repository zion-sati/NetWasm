// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System;

internal static class ObjectIdentityRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int GetHashCode(object? value);
}
