// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class AtomicInstructionEmitter(
    ITargetLayout layouts,
    ICilTypeOperandResolver types,
    INullDereferenceCheckEmitter nullChecks,
    IReferenceWriteBarrierEmitter writeBarrier) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.CompareExchange,
            InstructionFamily.ArraysFieldsStatics,
            EmitCompareExchange),
    ];

    // Loads the location, stores the value where it equals the comparand, and leaves what it loaded: a reference, an
    // integer of any width (the comparand narrowed to the width as the location holds it) or a float, compared by its
    // bits as desktop .NET does, so the NaN it holds matches the NaN it is given and +0 and -0 differ.
    private void EmitCompareExchange(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var address = request.Stack.Count - 3;
        var exchanged = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var resultKind = exchanged.StackKind;
        var encoding = SelectEncoding(exchanged);
        var temporary = encoding.UsesI64
            ? request.Context.NumericTemporaryI8
            : request.Context.NumericTemporaryI4;
        var addressLocal = GetStackLocal(
            request.Context,
            address,
            request.Stack[address]);
        var valueLocal = GetStackLocal(
            request.Context,
            address + 1,
            request.Stack[address + 1]);
        var comparandLocal = GetStackLocal(
            request.Context,
            address + 2,
            request.Stack[address + 2]);
        var resultLocal = GetStackLocal(
            request.Context,
            address,
            resultKind);
        // A null location throws NullReferenceException, as on desktop, unless analysis proved it non-null or its
        // loop checked it at entry.
        nullChecks.Emit(request, code, addressLocal, request.Stack[address]);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(addressLocal))));
        code.Write(WasmInstruction.WithOperand(
            encoding.Load,
            WasmInstructionOperand.Memory(encoding.Alignment, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)temporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)temporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(comparandLocal))));
        foreach (var instruction in encoding.Comparand)
        {
            code.Write(instruction);
        }
        code.Write(WasmInstruction.NoOperand(encoding.Equal));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(addressLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        code.Write(WasmInstruction.WithOperand(
            encoding.Store,
            WasmInstructionOperand.Memory(encoding.Alignment, 0)));
        // An exchange that stored a reference is a store like any other.
        writeBarrier.Emit(request, code, addressLocal, exchanged);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)temporary)));
        if (encoding.Result is { } result)
        {
            code.Write(WasmInstruction.NoOperand(result));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(resultLocal))));
        request.Stack.RemoveRange(address + 1, 2);
        request.Stack[address] = resultKind;
    }

    private AtomicValueEncoding SelectEncoding(CliTypeIdentity exchanged)
    {
        var kind = exchanged.StackKind;
        return exchanged.CanonicalName switch
        {
            "primitive:u1" => Narrow(WasmOpcodes.I32Load8Unsigned, WasmOpcodes.I32Store8, 0,
                [WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0xff)),
                 WasmInstruction.NoOperand(WasmOpcodes.I32And)]),
            "primitive:i1" => Narrow(WasmOpcodes.I32Load8Signed, WasmOpcodes.I32Store8, 0,
                [WasmInstruction.NoOperand(WasmOpcodes.I32Extend8Signed)]),
            "primitive:u2" => Narrow(WasmOpcodes.I32Load16Unsigned, WasmOpcodes.I32Store16, 1,
                [WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0xffff)),
                 WasmInstruction.NoOperand(WasmOpcodes.I32And)]),
            "primitive:i2" => Narrow(WasmOpcodes.I32Load16Signed, WasmOpcodes.I32Store16, 1,
                [WasmInstruction.NoOperand(WasmOpcodes.I32Extend16Signed)]),
            // The location's bits load as an integer and compare with the comparand's; the value stores as itself.
            "primitive:f4" => new(WasmOpcodes.I32Load, WasmOpcodes.F32Store, WasmOpcodes.I32Equal, 2, UsesI64: false,
                [WasmInstruction.NoOperand(WasmOpcodes.I32ReinterpretF32)], WasmOpcodes.F32ReinterpretI32),
            "primitive:f8" => new(WasmOpcodes.I64Load, WasmOpcodes.F64Store, WasmOpcodes.I64Equal, 3, UsesI64: true,
                [WasmInstruction.NoOperand(WasmOpcodes.I64ReinterpretF64)], WasmOpcodes.F64ReinterpretI64),
            _ => Whole(kind),
        };

        static AtomicValueEncoding Narrow(byte load, byte store, uint alignment, ImmutableArray<WasmInstruction> comparand) =>
            new(load, store, WasmOpcodes.I32Equal, alignment, UsesI64: false, comparand, null);
    }

    private AtomicValueEncoding Whole(CliValueKind kind)
    {
        var usesI64 = kind == CliValueKind.I8 ||
            (layouts.Target.UsesMemory64 &&
                kind is CliValueKind.NativeInt or CliValueKind.ManagedReference or
                    CliValueKind.ManagedAddress);
        if (kind is not (CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.NativeInt or CliValueKind.ManagedReference or
            CliValueKind.ManagedAddress))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                $"compare-exchange value kind '{kind}' is not supported"));
        }
        return usesI64
            ? new(WasmOpcodes.I64Load, WasmOpcodes.I64Store, WasmOpcodes.I64Equal, 3, UsesI64: true, [], null)
            : new(WasmOpcodes.I32Load, WasmOpcodes.I32Store, WasmOpcodes.I32Equal, 2, UsesI64: false, [], null);
    }

    // How the location loads and stores, how the comparand is brought to what the load gives, and how the loaded
    // value becomes the result's kind (a float's bits back to the float).
    private readonly record struct AtomicValueEncoding(
        byte Load,
        byte Store,
        byte Equal,
        uint Alignment,
        bool UsesI64,
        ImmutableArray<WasmInstruction> Comparand,
        byte? Result);

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
