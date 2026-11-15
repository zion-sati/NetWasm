// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IImplicitExceptionEmitter
{
    // Throws through the module's shared throw function. Wasmtime saves every callee-saved
    // register on each call to a function that contains a throw instruction, so the throw
    // stays out of line.
    void Emit(
        IWasmInstructionWriter code,
        ManagedExceptionKind kind,
        OptionalFunctionIndex throwFunctionIndex);
}
