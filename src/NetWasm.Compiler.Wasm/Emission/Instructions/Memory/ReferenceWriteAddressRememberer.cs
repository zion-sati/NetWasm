// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

// The copy is in the method's address scratch local, which no store sequence uses between the two.
internal sealed class ReferenceWriteAddressRememberer(IReferenceWriteRecordPolicy records) : IReferenceWriteAddressRememberer
{
    private readonly IReferenceWriteRecordPolicy _records = records ?? throw new ArgumentNullException(nameof(records));

    public void Remember(InstructionEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity stored)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(stored);
        if (!_records.Records(request, stored)) return;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
    }
}
