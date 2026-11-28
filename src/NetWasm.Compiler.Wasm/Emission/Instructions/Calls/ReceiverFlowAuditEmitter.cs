// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface IReceiverFlowAuditEmitter
{
    // Before an audited call dispatches: a receiver whose type id is not one of the call's audited receivers' fails
    // fast with the call's message. Writes nothing for a call that is not audited.
    void Emit(IWasmInstructionWriter code, DispatchCallSiteModel site, int receiverLocal, int typeIdLocal,
        IFunctionIndexResolver functionIndices);
}

// The receiver flow audit (RECEIVER_TYPE_FLOW_DESIGN.md, step B): the receiver's type id against the ids of the types
// the analysis found reach the call, as dispatch tests it against its cases; outside them, CoreLib's
// ReceiverFlowAudit.Fail with the call's message and the receiver's type id, which throws.
internal sealed class ReceiverFlowAuditEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IStaticDataLayout staticData,
    IKeyedDispatchEmitter keyedDispatch) : IReceiverFlowAuditEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly ITypeLayoutProvider _typeLayouts = typeLayouts ?? throw new ArgumentNullException(nameof(typeLayouts));
    private readonly IStaticDataLayout _staticData = staticData ?? throw new ArgumentNullException(nameof(staticData));
    private readonly IKeyedDispatchEmitter _keyedDispatch =
        keyedDispatch ?? throw new ArgumentNullException(nameof(keyedDispatch));

    public void Emit(IWasmInstructionWriter code, DispatchCallSiteModel site, int receiverLocal, int typeIdLocal,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(functionIndices);
        if (site.AuditedReceivers.IsDefault || site.AuditMessage is not { } message || site.AuditFailure is not { } failure)
            return;
        var ids = site.AuditedReceivers.Select(type => _typeLayouts.GetObjectLayout(type).TypeId).Distinct().Order()
            .ToArray();
        var ranges = Ranges(ids);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        _keyedDispatch.Emit(code, new(typeIdLocal, wide: false, ranges, static _ => { }, () =>
        {
            var address = _staticData.GetStringLayout(message).Address;
            code.Write(_layouts.Target.UsesMemory64
                ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(address))
                : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(address)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(failure))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        }));
    }

    // Consecutive ids as one range each.
    private static System.Collections.Immutable.ImmutableArray<KeyRange> Ranges(int[] ids)
    {
        var ranges = System.Collections.Immutable.ImmutableArray.CreateBuilder<KeyRange>();
        for (var index = 0; index < ids.Length; index++)
        {
            var low = ids[index];
            while (index + 1 < ids.Length && ids[index + 1] == ids[index] + 1) index++;
            ranges.Add(new KeyRange(low, ids[index]));
        }
        return ranges.ToImmutable();
    }
}
