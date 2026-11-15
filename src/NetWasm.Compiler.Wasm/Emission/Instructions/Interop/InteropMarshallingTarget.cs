// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed record InteropMarshallingTarget(InteropImportPlan Imports)
{
    // The module's object allocation policy where the marshalling code is
    // managed code; generated functions call allocate.
    public ObjectAllocationPolicy? ObjectAllocation { get; init; }
}
