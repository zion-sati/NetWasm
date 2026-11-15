// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices;

using System.Runtime.InteropServices.WebAssembly;

internal interface ICanonicalBufferAllocator
{
    CanonicalBuffer Allocate(nuint size, nuint alignment);
}
