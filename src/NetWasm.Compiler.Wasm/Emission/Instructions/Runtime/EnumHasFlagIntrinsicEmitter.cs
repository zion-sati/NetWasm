// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumHasFlagIntrinsicEmitter(
    IEnumHasFlagEmitter emitter) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var context = request.Instruction.Context;
        var receiverKind = request.Instruction.Stack[request.ArgumentBase];
        emitter.EmitHasFlag(
            code,
            request.Local(0, receiverKind),
            request.Local(1, CliValueKind.ManagedReference),
            request.Local(0, CliValueKind.I4),
            context.NumericTemporaryI4,
            context.ManagedThrowFunction,
            receiverKind,
            request.ConstrainedType);
    }
}
