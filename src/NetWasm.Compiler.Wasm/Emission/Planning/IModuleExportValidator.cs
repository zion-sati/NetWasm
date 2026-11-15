// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IModuleExportValidator
{
    void Validate(
        IReadOnlyCollection<WasmExport> exports,
        IReadOnlyDictionary<string, int> nativeCallbacks);
}
