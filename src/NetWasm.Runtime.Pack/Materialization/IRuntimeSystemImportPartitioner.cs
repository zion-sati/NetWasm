// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeSystemImportPartitioner
{
    RuntimeSystemImportPartition Partition(ImmutableArray<RuntimeNativeImport> imports);
}

// SystemExports names the C library functions, which the runtime exports; NativeImports are the imports a native
// library provides.
internal sealed record RuntimeSystemImportPartition(
    ImmutableArray<RuntimeNativeImport> NativeImports,
    ImmutableArray<string> SystemExports);
