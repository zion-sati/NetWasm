// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetNameEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IValueLayoutProvider values,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments,
    IKeyedDispatchEmitter keyedDispatch) : IEnumGetNameEmitter
{
    public void EmitGetName(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 0)
        {
            EmitTypeBased(request, code);
            return;
        }
        if (request.Method.MethodArguments.Length != 1)
            throw new InvalidOperationException("enum get-name intrinsic requires one enum type argument");

        var enumType = request.Method.MethodArguments[0];
        var entry = metadata.EnumMetadata
            .SingleOrDefault(candidate => candidate.EnumType.Equals(enumType));
        if (entry.TypeId == 0)
            throw new InvalidOperationException($"enum metadata is unavailable for '{enumType.CanonicalName}'");

        var storageKind = entry.UnderlyingType.StackKind;
        var input = request.Local(0, storageKind);
        var result = request.Instruction.Context.ObjectTemporary;
        addresses.Emit(code, 0);
        Set(code, result);
        // The value picks the first member that has it, searched among the members' values.
        keyedDispatch.Emit(code, KeyedDispatch.Of(input, storageKind == CliValueKind.I8,
            entry.Members.Select(member => (unchecked((long)member.RawValue), (Action)(() =>
            {
                addresses.Emit(code, member.NameLayout.Address);
                Set(code, result);
            }))),
            () => { }));
        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
        static void Get(IWasmInstructionWriter code, int local) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
        static void Set(IWasmInstructionWriter code, int local) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    }

    private void EmitTypeBased(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var enumType = request.Local(0, CliValueKind.ManagedReference);
        var value = request.Local(1, CliValueKind.ManagedReference);
        var result = request.Instruction.Context.ObjectTemporary;
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, enumType, typeId, request.Instruction.Context.ManagedThrowFunction);
        addresses.Emit(code, 0);
        Set(code, result);
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            var definition = types.GetTypeDefinition(entry.Type);
            var underlying = definition.EnumUnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(
                layouts.Target.ObjectHeaderSize, layout.Alignment);
            cases.Add((entry.TypeId, () =>
            {
                // The boxed value picks the first member that has it, searched among the members' values; the type's id, read
                // already, leaves its local free for an int value.
                var key = underlying.StackKind == CliValueKind.I8 ? request.Instruction.Context.NumericTemporaryI8 : typeId;
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)value)));
                ManagedMemoryEmitter.EmitLoadByType(
                    code, layouts.Target, payload, underlying, layout.Size);
                Set(code, key);
                keyedDispatch.Emit(code, KeyedDispatch.Of(key, underlying.StackKind == CliValueKind.I8,
                    entry.Members.Select(member => (unchecked((long)member.RawValue), (Action)(() =>
                    {
                        addresses.Emit(code, member.NameLayout.Address);
                        Set(code, result);
                    }))),
                    () => { }));
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
