// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal interface IReferenceWriteRecordPolicy
{
    // Whether a store of a value of the type at this instruction is recorded for the collector.
    bool Records(InstructionEmissionRequest request, CliTypeIdentity stored);
}
