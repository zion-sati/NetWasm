// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class ReadOnlySpanInstructionEmitter(
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider instanceFields,
    IStaticFieldLayoutProvider staticFields,
    IAddressInstructionEmitter addresses) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.CreateReadOnlySpanFromRva,
            InstructionFamily.ValueObjectBlockMemory,
            EmitCreate),
    ];

    private void EmitCreate(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var span = request.Instruction.Operand as CilOperand.ReadOnlySpanData ??
            throw Invalid("RVA span creation metadata is missing.");
        var spanLayout = values.GetValueLayout(span.SpanType);
        var elementLayout = values.GetValueLayout(span.ElementType);
        var (referenceField, lengthField) = ResolveFields(span);
        var referenceLayout = instanceFields.GetFieldLayout(referenceField);
        var lengthLayout = instanceFields.GetFieldLayout(lengthField);
        var staticAddress = staticFields.GetStaticFieldLayout(span.Field).Address;

        Require(span.ElementCount > 0, "RVA span element count is invalid.");
        Require(
            checked(span.ElementCount * elementLayout.Size) ==
                span.Field.Definition.InitialData.Length,
            $"RVA span data size does not match '{span.ElementType}'.");
        Require(
            staticAddress % elementLayout.Alignment == 0,
            $"RVA span data for '{span.ElementType}' is not aligned.");
        Require(
            referenceLayout.Size == layouts.Target.AddressSize,
            "ReadOnlySpan<T> reference field has an invalid size.");
        Require(
            lengthLayout.Size == sizeof(int),
            "ReadOnlySpan<T> length field has an invalid size.");
        Require(
            referenceLayout.Offset >= 0 &&
                checked(referenceLayout.Offset + referenceLayout.Size) <= spanLayout.Size,
            "ReadOnlySpan<T> reference field is outside its value layout.");
        Require(
            lengthLayout.Offset >= 0 &&
                checked(lengthLayout.Offset + lengthLayout.Size) <= spanLayout.Size,
            "ReadOnlySpan<T> length field is outside its value layout.");
        Require(
            spanLayout.ByReferenceOffsets.Contains(referenceLayout.Offset),
            "ReadOnlySpan<T> reference field is missing from its byref layout.");

        var resultOffset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ValueFrame)));
        if (resultOffset != 0)
        {
            addresses.Emit(code, resultOffset);
            addresses.Emit(code, AddressOperation.Add);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        addresses.Emit(code, staticAddress);
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            referenceLayout.Offset,
            referenceField.FieldType,
            referenceLayout.Size);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(span.ElementCount)));
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            lengthLayout.Offset,
            lengthField.FieldType,
            lengthLayout.Size);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                request.Stack.Count,
                CliValueKind.ValueType))));
        request.Stack.Add(CliValueKind.ValueType);
    }

    private (FieldInstanceModel Reference, FieldInstanceModel Length) ResolveFields(
        CilOperand.ReadOnlySpanData span)
    {
        var definition = typeDefinitions.ResolveTypeIdentity(span.SpanType);
        var resolved = definition.Fields
            .Select(fields.GetField)
            .Select(field => new FieldInstanceModel(
                field,
                span.SpanType,
                field.SignatureType.Substitute(span.SpanType.TypeArguments)))
            .ToArray();
        var reference = resolved.SingleOrDefault(field => field.Definition.Name == "_reference");
        var length = resolved.SingleOrDefault(field => field.Definition.Name == "_length");
        if (reference is null || length is null ||
            !IsElementReference(reference.FieldType, span.ElementType) ||
            length.FieldType.CanonicalName != "primitive:i4")
        {
            throw Invalid("ReadOnlySpan<T> has an incompatible field contract.");
        }
        return (reference, length);
    }

    private static bool IsElementReference(
        CliTypeIdentity type,
        CliTypeIdentity elementType)
    {
        while (type.Shape == CliTypeShape.Modified)
        {
            type = type.ElementType!;
        }
        return type.Shape == CliTypeShape.ManagedByReference &&
            type.ElementType!.Equals(elementType);
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static CompilerException Invalid(string message) => new(new(
        DiagnosticCode.RuntimeContract,
        message));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Invalid(message);
        }
    }
}
