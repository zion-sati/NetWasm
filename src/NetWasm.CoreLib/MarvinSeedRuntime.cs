// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System;

// NetWasm target adaptation for Marvin's process seed. The compiler lowers
// this declaration directly to the runtime random_seed ABI import.
internal static class MarvinSeedRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern ulong Get();
}
