// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal interface IReferenceWriteAddressRememberer
{
    // For a store whose address is only on the stack, as an array element's is. Before the store, with that
    // address on top of the stack: keeps a copy of it when the store will be recorded, which the write barrier's
    // Emit without an address records after the store.
    void Remember(InstructionEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity stored);
}
