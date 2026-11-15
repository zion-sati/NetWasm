// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>The module index space an instruction operand names.</summary>
internal enum WasmIndexSpace
{
    Function,
    Type,
    Table,
    Memory,
    Global,
    Tag,
    Data,
    Element,
}
