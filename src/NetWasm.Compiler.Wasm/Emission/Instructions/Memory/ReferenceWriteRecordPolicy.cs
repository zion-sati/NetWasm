// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

// A store is recorded when the program's collector is told of stores, the stored type holds a reference, and the
// store is not into an object the method allocated with no safepoint since: that object is young, and a minor
// collection traces it whole.
internal sealed class ReferenceWriteRecordPolicy(IValueLayoutProvider values) : IReferenceWriteRecordPolicy
{
    private readonly IValueLayoutProvider _values = values ?? throw new ArgumentNullException(nameof(values));

    public bool Records(InstructionEmissionRequest request, CliTypeIdentity stored)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stored);
        return request.Target.RuntimeImportSelection.IncludeReferenceWriteBarrier &&
               HoldsReferences(stored) &&
               !request.Context.RootMap.UnrecordedStores.Contains(request.Instruction.Offset);
    }

    private bool HoldsReferences(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.ManagedReference => true,
        CliValueKind.ValueType => _values.GetValueLayout(type).ContainsReferences,
        _ => false,
    };
}
