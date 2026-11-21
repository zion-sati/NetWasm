// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

// Separates a program's imports of the C library from its native library imports. Every runtime links the C library
// (the target's system libraries), as every process on Linux has it, so an import of libc (CoreLib's Math and MathF
// call its libm) is a runtime export, not a native library a project must reference.
internal sealed class RuntimeSystemImportPartitioner : IRuntimeSystemImportPartitioner
{
    // The library name upstream's Interop.Libraries.libc gives the C library.
    public const string CLibrary = "libc";

    public RuntimeSystemImportPartition Partition(ImmutableArray<RuntimeNativeImport> imports)
    {
        if (imports.IsDefault || imports.Any(import => import is null))
            throw new InvalidOperationException("The native imports to partition are invalid.");
        return new(
            [.. imports.Where(import => !IsCLibrary(import))],
            [.. imports.Where(IsCLibrary).Select(import => import.EntryPoint).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)]);
    }

    private static bool IsCLibrary(RuntimeNativeImport import) =>
        string.Equals(import.LibraryName, CLibrary, StringComparison.Ordinal);
}
