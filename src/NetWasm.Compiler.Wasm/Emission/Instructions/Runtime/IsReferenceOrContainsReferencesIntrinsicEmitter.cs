// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class IsReferenceOrContainsReferencesIntrinsicEmitter(
    IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length != 1)
        {
            throw new InvalidOperationException(
                "IsReferenceOrContainsReferences requires one closed type argument");
        }
        var inspectedType = request.Method.MethodArguments[0];
        var containsReferences = !inspectedType.IsValueType ||
            inspectedType.Shape == CliTypeShape.ManagedByReference ||
            ContainsManagedReferences(values.GetValueLayout(inspectedType));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(containsReferences ? 1 : 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.I4)))));
    }

    private static bool ContainsManagedReferences(ValueLayout layout) =>
        layout.ContainsReferences || !layout.ByReferenceOffsets.IsEmpty;
}
