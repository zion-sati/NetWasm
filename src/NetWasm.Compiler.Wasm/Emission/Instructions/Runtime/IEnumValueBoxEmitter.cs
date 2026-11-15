// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumValueBoxEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        EnumMetadataLayout entry,
        int valueLocal,
        int resultLocal);
}
