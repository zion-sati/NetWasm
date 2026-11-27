// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.StructResults;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// What a delegate's invoke functions read from its Invoke signature. A value-type
// result is written through a hidden first parameter, so the delegate is the second
// parameter then; any other result is kept in a local of its kind.
internal sealed record DelegateInvokeSignature(bool ReturnsValue, bool ReturnsScalar, CliValueKind ResultType)
{
    // The fields of a value type Invoke returns in Wasm results, in field order; empty for any other return.
    public ImmutableArray<CliValueKind> StructResults { get; init; } = [];

    public int Receiver => ReturnsValue ? 1 : 0;

    public int FirstArgument => Receiver + 1;

    // The locals a target's result waits in until the invoke returns it: one per returned field, or the scalar's one
    // (declared, unused, for a void or buffered return).
    public ImmutableArray<CliValueKind> ResultLocals => StructResults.IsEmpty ? [ResultType] : StructResults;

    public static DelegateInvokeSignature Of(MethodInstanceModel invoke, IStructResultClassifier? structResults = null)
    {
        var returned = invoke.Signature.ReturnSignatureType;
        var shape = returned.StackKind == CliValueKind.ValueType ? structResults?.Classify(returned) : null;
        var returnsValue = returned.StackKind == CliValueKind.ValueType && shape is null;
        var returnsScalar = returned.StackKind != CliValueKind.ValueType && invoke.Signature.ReturnType != CliValueKind.Void;
        return new(returnsValue, returnsScalar, returnsScalar ? invoke.Signature.ReturnType : CliValueKind.I4)
        {
            StructResults = shape?.Results ?? [],
        };
    }

    // Takes a target's results off the stack into the result locals that start at the first.
    public void EmitKeep(IWasmInstructionWriter code, int firstResultLocal)
    {
        if (ReturnsScalar)
        {
            Write(code, WasmOpcodes.LocalSet, firstResultLocal);
            return;
        }
        for (var index = StructResults.Length - 1; index >= 0; index--) Write(code, WasmOpcodes.LocalSet, firstResultLocal + index);
    }

    // Pushes the kept results for the invoke's return.
    public void EmitReturned(IWasmInstructionWriter code, int firstResultLocal)
    {
        if (ReturnsScalar)
        {
            Write(code, WasmOpcodes.LocalGet, firstResultLocal);
            return;
        }
        for (var index = 0; index < StructResults.Length; index++) Write(code, WasmOpcodes.LocalGet, firstResultLocal + index);
    }

    private static void Write(IWasmInstructionWriter code, byte opcode, int local) =>
        code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));
}
