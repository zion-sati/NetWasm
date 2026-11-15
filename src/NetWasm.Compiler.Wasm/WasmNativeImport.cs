// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm;

/// <summary>A reached static native entry with its target-resolved physical signature.</summary>
public sealed record WasmNativeImport(
    string LibraryName,
    string EntryPoint,
    ImmutableArray<WasmValueType> Parameters,
    WasmValueType? ReturnType);
