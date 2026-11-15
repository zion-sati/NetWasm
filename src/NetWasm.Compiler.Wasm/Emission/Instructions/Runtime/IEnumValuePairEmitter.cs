// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumValuePairEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int left,
        int right);
}
