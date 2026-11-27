// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;

using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateArgumentRootEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IStructResultClassifier? structResults = null) : IDelegateArgumentRootEmitter
{
    public void Emit(IWasmInstructionWriter code, MethodInstanceModel invoke, int frameLocal)
    {
        var signature = DelegateInvokeSignature.Of(invoke, structResults);
        var sources = new List<(int Parameter, int Offset)> { (signature.Receiver, -1) };
        var layout = StructParameters.Layout(structResults, invoke.Signature.ParameterSignatureTypes, signature.FirstArgument);
        for (var index = 0; index < invoke.Signature.ParameterSignatureTypes.Length; index++)
        {
            var type = invoke.Signature.ParameterSignatureTypes[index];
            var (parameter, shape) = layout[index];
            if (type.StackKind == CliValueKind.ManagedReference)
            {
                sources.Add((parameter, -1));
            }
            else if (shape is not null)
            {
                // Passed by its fields: each reference field is a parameter of its own.
                for (var lane = 0; lane < shape.Fields.Length; lane++)
                {
                    if (shape.Fields[lane].Kind == CliValueKind.ManagedReference) sources.Add((parameter + lane, -1));
                }
            }
            else if (type.StackKind == CliValueKind.ValueType)
            {
                foreach (var offset in values.GetValueLayout(type).ReferenceOffsets)
                {
                    sources.Add((parameter, offset));
                }
            }
        }

        WriteI32Constant(code, sources.Count);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        WriteLocalSet(code, frameLocal);
        for (var slot = 0; slot < sources.Count; slot++)
        {
            (var parameter, var offset) = sources[slot];
            WriteLocalGet(code, frameLocal);
            if (slot != 0)
            {
                EmitAddressConstant(code, slot * layouts.Target.ObjectReferenceSize);
                code.Write(WasmInstruction.NoOperand(
                    layouts.Target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
            }
            WriteLocalGet(code, parameter);
            if (offset >= 0)
            {
                EmitReference(code, layouts.Target.UsesMemory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
                    offset);
            }
            EmitReference(code, layouts.Target.UsesMemory64 ? WasmOpcodes.I64Store : WasmOpcodes.I32Store, 0);
        }
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value) =>
        code.Write(layouts.Target.UsesMemory64
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));

    private void EmitReference(IWasmInstructionWriter code, byte opcode, int offset) =>
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(layouts.Target.UsesMemory64 ? 3u : 2u, checked((uint)offset))));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteCall(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)index))));
}
