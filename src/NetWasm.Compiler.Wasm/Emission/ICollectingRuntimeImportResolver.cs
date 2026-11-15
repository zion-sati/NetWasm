// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICollectingRuntimeImportResolver
{
    // The runtime functions that can collect, by the index a call to one names
    // in a module with the selected imports.
    FrozenDictionary<uint, RuntimeImportSymbol> Resolve(RuntimeImportSelection selection);
}
