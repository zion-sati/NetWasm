using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetNamesEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments) : IEnumGetNamesEmitter
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
        EmitArray(code, result, temporary, entry);
    }

    private void EmitTypeBased(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var temporary = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, temporary);
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        foreach (var entry in metadata.EnumMetadata)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)temporary)));
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitArray(code, result, temporary, entry);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
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
        EnumMetadataLayout entry)
    {
        WriteI32(code, entry.Members.Length);
        WriteI32(code, typeLayouts.ReferenceArrayTypeId);
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
                code, layouts.Target, result, temporary, layouts.Target.ObjectReferenceSize);
            addresses.Emit(code, entry.Members[index].NameLayout.Address);
            ManagedMemoryEmitter.EmitStoreBySize(
                code, layouts.Target, 0, layouts.Target.ObjectReferenceSize);
        }
    }

    private EnumMetadataLayout Find(CliTypeIdentity enumType) =>
        metadata.EnumMetadata
            .Where(candidate => candidate.Type.Assembly.Equals(enumType.Assembly))
            .SingleOrDefault(candidate =>
            {
                var definition = types.GetTypeDefinition(candidate.Type);
                return definition.FullName == enumType.FullName;
            }) is { TypeId: not 0 } match
            ? match
            : throw new InvalidOperationException(
                $"enum metadata is unavailable for '{enumType.CanonicalName}'");

    private static void WriteI32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
