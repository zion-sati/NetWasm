// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class CallableLoadingEmitter(
    IFunctionLoader functions,
    IVirtualFunctionLoader virtualFunctions) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new InstructionCommand(
            CilOperation.LoadFunction,
            InstructionFamily.CallsAndCallableLoading,
            ForwardFunction),
        new InstructionCommand(
            CilOperation.LoadVirtualFunction,
            InstructionFamily.CallsAndCallableLoading,
            ForwardVirtualFunction),
    ];

    private void ForwardFunction(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices) => functions.Load(request, code, functionIndices);

    private void ForwardVirtualFunction(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices) =>
        virtualFunctions.Load(request, code, functionIndices);
}
