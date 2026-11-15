// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices;

internal static class ConditionalWeakTableRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int Create(object? key, object? value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern object? GetKey(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern object? GetValue(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void SetTargetToNull(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void SetValue(int handle, object? value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Release(int handle);
}
