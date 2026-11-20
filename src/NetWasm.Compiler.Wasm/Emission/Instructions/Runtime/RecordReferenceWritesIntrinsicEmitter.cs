// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Buffer.RecordReferenceWrites follows a block move of references, which stores none of them one by one: the
// write barrier over the destination's range, for a program whose collector has generations. For every other
// collector the call is nothing.
internal sealed class RecordReferenceWritesIntrinsicEmitter(IReferenceWriteBarrierEmitter writeBarrier)
    : IRuntimeIntrinsicEmitter
{
    private readonly IReferenceWriteBarrierEmitter _writeBarrier = writeBarrier ??
        throw new ArgumentNullException(nameof(writeBarrier));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        _writeBarrier.Emit(request.Instruction, code,
            request.Local(0, CliValueKind.ManagedAddress), request.Local(1, CliValueKind.NativeInt));
    }
}
