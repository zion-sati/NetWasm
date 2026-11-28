// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.CompilerServices
{
    // The compiler's mark on an address it has shown is not 0, so an access through the result needs no
    // NullReferenceException check: loop versioning wraps the addresses a test ahead of the loop covered. A call compiles
    // to nothing; the body only says what it returns.
    internal static class NonZeroAddress
    {
        internal static ref T Of<T>(ref T address) => ref address;
    }
}
