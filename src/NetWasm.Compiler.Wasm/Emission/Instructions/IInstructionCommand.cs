// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IInstructionCommand
{
    CilOperation Operation { get; }
    InstructionFamily Family { get; }

    // Whether the command publishes its instruction's safepoint roots itself,
    // instead of the dispatcher publishing them before it runs.
    bool PublishesRoots { get; }

    void Emit(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices);
}
