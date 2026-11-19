// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetNamesEmitter(
    IEnumMetadataSource metadata,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments,
    IKeyedDispatchEmitter keyedDispatch) : IEnumGetNamesEmitter
{
    public void EmitGetNames(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 0)
        {
            EmitTypeBased(request, code);
            return;
        }
        if (request.Method.MethodArguments.Length != 1)
        {
            throw new InvalidOperationException(
                "enum get-names intrinsic requires one closed enum type argument");
        }

        var enumType = request.Method.MethodArguments[0];
        var entry = Find(enumType);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var temporary = request.Instruction.Context.NumericTemporaryI4;
        EmitArray(code, result, temporary, entry, request.Instruction.Context.InlineArrayData);
    }

    private void EmitTypeBased(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4Second;
        var temporary = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, typeId, request.Instruction.Context.ManagedThrowFunction);
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            cases.Add((entry.TypeId, () =>
            {
                EmitArray(code, result, temporary, entry, request.Instruction.Context.InlineArrayData);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)result)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
    }

    private void EmitArray(
        IWasmInstructionWriter code,
        int result,
        int temporary,
        EnumMetadataLayout entry,
        bool inlineData)
    {
        WriteI32(code, entry.Members.Length);
        WriteI32(code, typeLayouts.GetObjectLayout(CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false))).TypeId);
        WriteI32(code, typeLayouts.StringTypeId);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateReferenceArray))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        for (var index = 0; index < entry.Members.Length; index++)
        {
            WriteI32(code, index);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)temporary)));
            ManagedMemoryEmitter.EmitArrayElementAddress(
                code, layouts.Target, result, temporary, inlineData, layouts.Target.ObjectReferenceSize);
            addresses.Emit(code, entry.Members[index].NameLayout.Address);
            ManagedMemoryEmitter.EmitStoreBySize(
                code, layouts.Target, 0, layouts.Target.ObjectReferenceSize);
        }
    }

    private EnumMetadataLayout Find(CliTypeIdentity enumType) =>
        metadata.EnumMetadata
            .SingleOrDefault(candidate => candidate.EnumType.Equals(enumType)) is { TypeId: not 0 } match
            ? match
            : throw new InvalidOperationException(
                $"enum metadata is unavailable for '{enumType.CanonicalName}'");

    private static void WriteI32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
