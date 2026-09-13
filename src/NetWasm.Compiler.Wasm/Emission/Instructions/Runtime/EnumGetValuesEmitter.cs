using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetValuesEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IValueLayoutProvider values,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments) : IEnumGetValuesEmitter
{
    public void EmitGetValues(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 0)
        {
            EmitTypeBased(request, code);
            return;
        }
        if (request.Method.MethodArguments.Length != 1)
            throw new InvalidOperationException("enum get-values intrinsic requires one enum type argument");
        var enumType = request.Method.MethodArguments[0];
        var entry = metadata.EnumMetadata
            .Where(candidate => candidate.Type.Assembly.Equals(enumType.Assembly))
            .SingleOrDefault(candidate => types.GetTypeDefinition(candidate.Type).FullName == enumType.FullName);
        if (entry.TypeId == 0)
            throw new InvalidOperationException($"enum metadata is unavailable for '{enumType.CanonicalName}'");

        var result = request.Local(0, CliValueKind.ManagedReference);
        var temporary = request.Instruction.Context.NumericTemporaryI4;
        var definition = types.GetTypeDefinition(entry.Type);
        var isUnderlying = request.Method.Definition.Name ==
            "InternalGetValuesAsUnderlyingType";
        var elementType = isUnderlying ? definition.EnumUnderlyingType : enumType;
        var elementLayout = values.GetValueLayout(elementType);
        var arrayTypeId = typeLayouts.GetObjectLayout(
            CliTypeIdentity.SzArray(elementType)).TypeId;
        WriteI32(code, entry.Members.Length);
        WriteI32(code, arrayTypeId);
        WriteI32(code, typeLayouts.GetObjectLayout(elementType).TypeId);
        WriteI32(code, elementLayout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateValueArray))));
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
                code, layouts.Target, result, temporary, elementLayout.Size);
            if (elementType.StackKind == CliValueKind.I8)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant,
                    WasmInstructionOperand.Signed64(unchecked((long)entry.Members[index].RawValue))));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Store,
                    WasmInstructionOperand.Memory(3, 0)));
            }
            else
            {
                WriteI32(code, unchecked((int)entry.Members[index].RawValue));
                ManagedMemoryEmitter.EmitStoreBySize(
                    code, layouts.Target, 0, elementLayout.Size);
            }
        }
    }

    private void EmitTypeBased(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var underlyingType = request.Local(1, CliValueKind.I4);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var temporary = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, temporary);
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
        foreach (var entry in metadata.EnumMetadata)
        {
            var definition = types.GetTypeDefinition(entry.Type);
            var element = definition.EnumUnderlyingType;
            var elementLayout = values.GetValueLayout(element);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)temporary)));
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)underlyingType)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitArray(
                code,
                result,
                temporary,
                entry,
                element,
                elementLayout,
                GetArrayTypeId(element),
                typeLayouts.GetObjectLayout(element).TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            var enumType = EnumType(entry);
            var enumLayout = values.GetValueLayout(enumType);
            EmitArray(
                code,
                result,
                temporary,
                entry,
                enumType,
                enumLayout,
                typeLayouts.ReferenceArrayTypeId,
                entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void EmitArray(
        IWasmInstructionWriter code,
        int result,
        int temporary,
        EnumMetadataLayout entry,
        CliTypeIdentity element,
        ValueLayout elementLayout,
        int arrayTypeId,
        int elementTypeId)
    {
        WriteI32(code, entry.Members.Length);
        WriteI32(code, arrayTypeId);
        WriteI32(code, elementTypeId);
        WriteI32(code, elementLayout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateValueArray))));
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
                code, layouts.Target, result, temporary, elementLayout.Size);
            if (element.StackKind == CliValueKind.I8)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant,
                    WasmInstructionOperand.Signed64(unchecked((long)entry.Members[index].RawValue))));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Store,
                    WasmInstructionOperand.Memory(3, 0)));
            }
            else
            {
                WriteI32(code, unchecked((int)entry.Members[index].RawValue));
                ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, elementLayout.Size);
            }
        }
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));

    private CliTypeIdentity EnumType(EnumMetadataLayout entry)
    {
        var definition = types.GetTypeDefinition(entry.Type);
        return CliTypeIdentity.Named(
            entry.Type.Assembly,
            definition.Namespace,
            definition.Name,
            isValueType: true,
            definition.EnumUnderlyingType.StackKind);
    }

    private int GetArrayTypeId(CliTypeIdentity element)
    {
        try
        {
            return typeLayouts.GetObjectLayout(CliTypeIdentity.SzArray(element)).TypeId;
        }
        catch (CompilerException exception) when (
            exception.Diagnostic.Code == DiagnosticCode.RuntimeContract)
        {
            // Type-based Enum.GetValues returns Array. If a particular closed
            // array identity is not otherwise reachable, the base Array
            // descriptor is sufficient for that uncast branch.
            return typeLayouts.ReferenceArrayTypeId;
        }
    }
}
