// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IInstructionCommandFactory
{
    InstructionCommand Create(
        CilOperation operation,
        InstructionFamily family,
        Action<InstructionEmissionRequest, IWasmInstructionWriter,
            IFunctionIndexResolver> emit);
}

internal sealed class InstructionCommandFactory : IInstructionCommandFactory
{
    public InstructionCommand Create(
        CilOperation operation,
        InstructionFamily family,
        Action<InstructionEmissionRequest, IWasmInstructionWriter,
            IFunctionIndexResolver> emit) =>
        new(operation, family, emit);
}
