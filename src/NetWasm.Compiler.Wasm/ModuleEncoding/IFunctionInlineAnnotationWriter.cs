// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public interface IFunctionInlineAnnotationWriter
{
    void Write(IWasmBinaryWriter output, IReadOnlyList<WasmFunctionDefinition> functions, int functionImportCount);
}
