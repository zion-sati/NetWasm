// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IRuntimeImportResolver
{
    int Resolve(RuntimeImportSymbol symbol);

    int Resolve(RuntimeImportSymbol symbol, WasmModuleProfile profile);

    int Resolve(RuntimeImportSymbol symbol, RuntimeImportSelection selection) =>
        Resolve(symbol, selection.ModuleProfile);

    ImmutableArray<WasmFunctionImport> Resolve(WasmModuleProfile profile);

    ImmutableArray<WasmFunctionImport> Resolve(RuntimeImportSelection selection) =>
        Resolve(selection.ModuleProfile);
}
