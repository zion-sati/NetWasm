using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumToStringEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IValueLayoutProvider values,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumValueFormatter valueFormatter,
    IEnumTypeArgumentValidator typeArguments) : IEnumToStringEmitter
{
    public void EmitToString(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.Definition.Name == "InternalFormat")
        {
            EmitTypeBased(request, code);
            return;
        }

        var receiverKind = request.Instruction.Stack[request.ArgumentBase];
        if (receiverKind == CliValueKind.ManagedAddress)
        {
            EmitConstrained(request, code);
            return;
        }

        EmitBoxed(request, code);
    }

    private void EmitTypeBased(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var enumType = request.Local(0, CliValueKind.ManagedReference);
        var value = request.Local(1, CliValueKind.ManagedReference);
        var format = request.Local(2, CliValueKind.ManagedReference);
        var result = request.Instruction.Context.ObjectTemporary;
        var typeId = request.Instruction.Context.NumericTemporaryI4;

        typeArguments.Validate(code, enumType, typeId);
        addresses.Emit(code, 0);
        Set(code, result);

        foreach (var entry in metadata.EnumMetadata)
        {
            var definition = types.GetTypeDefinition(entry.Type);
            var underlying = definition.EnumUnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, layout.Alignment);
            EmitTypeMatch(code, typeId, entry.TypeId);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            valueFormatter.Emit(
                code, entry, underlying, value, payload, format, result,
                request.Instruction.Context.NumericTemporaryI8,
                request.Instruction.Context.NumericTemporaryI4);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private void EmitBoxed(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var value = request.Local(0, CliValueKind.ManagedReference);
        var format = GetFormatLocal(request);
        var result = request.Instruction.Context.ObjectTemporary;
        var typeId = request.Instruction.Context.NumericTemporaryI4;

        Get(code, value);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)typeId)));
        addresses.Emit(code, 0);
        Set(code, result);

        foreach (var entry in metadata.EnumMetadata)
        {
            var definition = types.GetTypeDefinition(entry.Type);
            var underlying = definition.EnumUnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, layout.Alignment);
            EmitTypeMatch(code, typeId, entry.TypeId);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            valueFormatter.Emit(
                code, entry, underlying, value, payload, format, result,
                request.Instruction.Context.NumericTemporaryI8,
                request.Instruction.Context.NumericTemporaryI4);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private static void EmitTypeMatch(
        IWasmInstructionWriter code,
        int typeId,
        int expectedTypeId)
    {
        Get(code, typeId);
        WriteI32(code, expectedTypeId);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
    }

    private static int? GetFormatLocal(RuntimeIntrinsicEmissionRequest request)
    {
        if (request.Method.Definition.Name == "InternalToString")
            return request.Local(1, CliValueKind.ManagedReference);

        var parameterTypes = request.Method.Signature.ParameterSignatureTypes;
        return parameterTypes.Length > 0 &&
            parameterTypes[0] is var first &&
            (first.CanonicalName == "primitive:string" ||
             first.FullName == "System.String")
            ? request.Local(1, CliValueKind.ManagedReference)
            : null;
    }

    private void EmitConstrained(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var enumType = request.ConstrainedType ?? throw new InvalidOperationException(
            "constrained enum formatting requires its closed receiver type");
        var entry = metadata.EnumMetadata
            .Where(candidate => candidate.Type.Assembly.Equals(enumType.Assembly))
            .SingleOrDefault(candidate =>
                types.GetTypeDefinition(candidate.Type).FullName == enumType.FullName);
        if (entry.TypeId == 0)
        {
            throw new InvalidOperationException(
                $"enum metadata is unavailable for '{enumType.CanonicalName}'");
        }

        var definition = types.GetTypeDefinition(entry.Type);
        var underlying = definition.EnumUnderlyingType;
        var layout = values.GetValueLayout(underlying);
        var value = request.Local(0, CliValueKind.ManagedAddress);
        var format = GetFormatLocal(request);
        var result = request.Instruction.Context.ObjectTemporary;
        addresses.Emit(code, 0);
        Set(code, result);
        valueFormatter.Emit(
            code, entry, underlying, value, 0, format, result,
            request.Instruction.Context.NumericTemporaryI8,
            request.Instruction.Context.NumericTemporaryI4);
        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
