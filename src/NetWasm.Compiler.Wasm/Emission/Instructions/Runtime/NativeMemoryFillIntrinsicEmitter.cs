// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryFillIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        SmallBlockEmitter.EmitFill(
            code,
            request.Target,
            request.Instruction.Target.InlineSmallBlockOperations,
            destination: request.Local(0, CliValueKind.NativeInt),
            value: request.Local(2, CliValueKind.I4),
            byteCount: request.Local(1, CliValueKind.NativeInt));
}
