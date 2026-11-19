// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumIsDefinedEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IValueLayoutProvider values,
    ITargetLayout layouts,
    IEnumTypeArgumentValidator typeArguments,
    IKeyedDispatchEmitter keyedDispatch) : IEnumIsDefinedEmitter
{
    public void EmitIsDefined(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 0)
        {
            EmitTypeBased(request, code);
            return;
        }
        if (request.Method.MethodArguments.Length != 1)
            throw new InvalidOperationException("enum is-defined intrinsic requires one enum type argument");
        var enumType = request.Method.MethodArguments[0];
        var entry = metadata.EnumMetadata
            .SingleOrDefault(candidate => candidate.EnumType.Equals(enumType));
        if (entry.TypeId == 0)
            throw new InvalidOperationException($"enum metadata is unavailable for '{enumType.CanonicalName}'");

        var storageKind = entry.UnderlyingType.StackKind;
        var input = request.Local(0, storageKind);
        var result = request.Local(0, CliValueKind.I4);
        var preservedInput = storageKind == CliValueKind.I8
            ? request.Instruction.Context.NumericTemporaryI8
            : request.Instruction.Context.NumericTemporaryI4;
        Get(code, input);
        Set(code, preservedInput);
        WriteI32(code, 0);
        Set(code, result);
        // The value picks whether a member has it, searched among the members' values.
        keyedDispatch.Emit(code, KeyedDispatch.Of(preservedInput, storageKind == CliValueKind.I8,
            entry.Members.Select(member => (unchecked((long)member.RawValue), (Action)(() =>
            {
                WriteI32(code, 1);
                Set(code, result);
            }))),
            () => { }));
        // The answer stays in the result local, as for every intrinsic; nothing is left on the Wasm stack.
        static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        static void Get(IWasmInstructionWriter code, int local) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
        static void Set(IWasmInstructionWriter code, int local) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    }

    private void EmitTypeBased(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var value = request.Local(1, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.I4);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, typeId, request.Instruction.Context.ManagedThrowFunction);
        WriteI32(code, 0);
        Set(code, result);
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = types.GetTypeDefinition(entry.Type).EnumUnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, layout.Alignment);
            cases.Add((entry.TypeId, () =>
            {
                // The boxed value picks whether a member has it, searched among the members' values; the type's id, read already,
                // leaves its local free for an int value.
                var key = underlying.StackKind == CliValueKind.I8 ? request.Instruction.Context.NumericTemporaryI8 : typeId;
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)value)));
                ManagedMemoryEmitter.EmitLoadByType(code, layouts.Target, payload, underlying, layout.Size);
                Set(code, key);
                keyedDispatch.Emit(code, KeyedDispatch.Of(key, underlying.StackKind == CliValueKind.I8,
                    entry.Members.Select(member => (unchecked((long)member.RawValue), (Action)(() =>
                    {
                        WriteI32(code, 1);
                        Set(code, result);
                    }))),
                    () => { }));
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));


    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
