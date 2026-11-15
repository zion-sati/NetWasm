// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumHashCodeEmitter
{
    void EmitHashCode(
        IWasmInstructionWriter code,
        CliValueKind receiverKind,
        CliTypeIdentity? receiverType,
        int receiver,
        int result,
        int temporaryI4,
        int temporaryI8,
        OptionalFunctionIndex throwFunction);
}
