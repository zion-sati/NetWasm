// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IHostCallbackStringArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target,
        OptionalFunctionIndex throwFunction);
}

/// <summary>Wraps a host object handle in its managed <c>JSObject</c>, which then owns it.</summary>
internal interface IHostCallbackHostObjectArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity objectType,
        int handleParameter,
        int destination,
        OptionalFunctionIndex throwFunction);
}

internal interface IHostCallbackByteArrayArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity arrayType,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target,
        OptionalFunctionIndex throwFunction);
}
