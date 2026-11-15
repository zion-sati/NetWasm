// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class StackStorageAnchorValueEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter addresses) : IStackStorageAnchorValueEmitter
{
    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int anchor)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegative(anchor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(anchor, context.RootMap.AnchorPlan.Anchors.Length);
        if (context.FilterEnvironment.AnchorOffsets.TryGetValue(anchor, out var offset))
        {
            addresses.Emit(code, context, offset);
            ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target, 0, layouts.Target.AddressSize);
        }
        else if (context.AnchorLocals.TryGetValue(anchor, out var local))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(checked((uint)local))));
        }
        else
        {
            throw new InvalidOperationException("A backing anchor has no physical storage.");
        }
    }
}
