// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

// The system libraries a program links: its native archives that throw or catch C++ exceptions take the pack's C++
// exception closure (libc++, libc++abi and libunwind with Wasm exception handling, and the standalone runtime
// without its aborting __cxa_throw) in place of the ordinary one; other programs keep the ordinary closure.
internal static class RuntimeExceptionSystemLibraries
{
    public static ImmutableArray<string> Select(
        ImmutableArray<string> systemLibraryPaths, ImmutableArray<string> exceptionLibraryPaths, bool cxxExceptions)
    {
        if (systemLibraryPaths.IsDefault || exceptionLibraryPaths.IsDefault)
            throw new ArgumentException("The system library paths are required.");
        if (!cxxExceptions)
            return systemLibraryPaths;
        if (exceptionLibraryPaths.IsEmpty)
            throw new InvalidOperationException(
                "A native library throws or catches C++ exceptions, and this runtime pack has no C++ exception libraries.");
        return exceptionLibraryPaths;
    }
}
