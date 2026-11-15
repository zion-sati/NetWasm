// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumEqualsEmitter
{
    void EmitEquals(
        IWasmInstructionWriter code,
        CliValueKind leftKind,
        CliTypeIdentity? constrainedType,
        int left,
        int right,
        int result,
        int temporaryReference,
        int temporaryI4,
        OptionalFunctionIndex throwFunction);
}
