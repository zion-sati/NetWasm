// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal interface IReferenceWriteBarrierEmitter
{
    // After a value of the type was stored into memory: tells the collector where, when the store is recorded. The
    // local holds the address stored through, or the start of the object or value stored into.
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, int addressLocal, CliTypeIdentity stored);
    // After a store whose address IReferenceWriteAddressRememberer kept: records it at that address.
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity stored);
    // After a store whose address no local holds and whose emitter uses the scratch local itself: records it
    // at the address the given instructions leave on the stack, which are written only when it is recorded.
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code,
        Action<IWasmInstructionWriter> address, CliTypeIdentity stored);
    // After references were stored into a range of memory: the locals hold its start and its length in bytes, at
    // the target's address width.
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, int addressLocal, int bytesLocal);
}

// A collector with generations traces the objects it kept at an earlier collection only where a reference has
// been stored into them since, so each such store is followed by a call that records where. The runtime's
// function is a load and a store, which the final link inlines. A program compiled for any other collector has
// neither the calls nor the imports.
//
// Whether a program records is its module's to say, not the method's: a filter funclet and a generated function
// are emitted through contexts of their own, and their stores are recorded like any other.
//
// One record covers one stored value however large: the collector traces the whole object, or the whole element
// of a large array, that the recorded address lies in. A null needs none, and a store that may be one is
// recorded all the same, since the record is cheaper than the test.
internal sealed class ReferenceWriteBarrierEmitter(
    IRuntimeImportResolver runtimeImports,
    IReferenceWriteRecordPolicy records) : IReferenceWriteBarrierEmitter
{
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly IReferenceWriteRecordPolicy _records = records ?? throw new ArgumentNullException(nameof(records));

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, int addressLocal, CliTypeIdentity stored) =>
        Emit(request, code, writer => writer.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)addressLocal))), stored);

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity stored)
    {
        ArgumentNullException.ThrowIfNull(request);
        Emit(request, code, request.Context.ObjectTemporary, stored);
    }

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code,
        Action<IWasmInstructionWriter> address, CliTypeIdentity stored)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(stored);
        if (!_records.Records(request, stored)) return;
        address(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
            (uint)_runtimeImports.Resolve(RuntimeImportSymbol.RecordReferenceWrite, request.Target.RuntimeImportSelection))));
    }

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, int addressLocal, int bytesLocal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var selection = request.Target.RuntimeImportSelection;
        if (!selection.IncludeReferenceWriteBarrier) return;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)addressLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)bytesLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
            (uint)_runtimeImports.Resolve(RuntimeImportSymbol.RecordReferenceWrites, selection))));
    }
}
