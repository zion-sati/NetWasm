// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

internal interface IStackTraceSourceLocationReader
{
    ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> Read(
        MetadataCompilationSnapshot metadata,
        CompilerOptions options);
}
