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

internal sealed class EnumToStringEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IMethodRepository methods,
    ITypeDescriptorSource descriptors,
    IValueLayoutProvider values,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments,
    IKeyedDispatchEmitter keyedDispatch) : IEnumToStringEmitter
{
    private readonly Lazy<EntityKey> _managedFormat = new(
        () => ResolveManagedFormat(descriptors, types, methods));

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

        typeArguments.Validate(code, enumType, typeId, request.Instruction.Context.ManagedThrowFunction);
        addresses.Emit(code, 0);
        Set(code, result);
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = entry.UnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(
                layouts.Target.ObjectHeaderSize, layout.Alignment);
            cases.Add((entry.TypeId, () =>
            {
                EmitManagedFormat(request, code, entry, value, payload, format, result);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));

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
        Set(code, typeId);
        addresses.Emit(code, 0);
        Set(code, result);
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = entry.UnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(
                layouts.Target.ObjectHeaderSize, layout.Alignment);
            cases.Add((entry.TypeId, () =>
            {
                EmitManagedFormat(request, code, entry, value, payload, format, result);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));

        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private void EmitConstrained(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var enumType = request.ConstrainedType ?? throw new InvalidOperationException(
            "enum formatting requires a closed receiver type");
        var entry = metadata.EnumMetadata
            .SingleOrDefault(candidate => candidate.EnumType.Equals(enumType));
        if (entry.TypeId == 0)
        {
            throw new InvalidOperationException(
                $"enum metadata is unavailable for '{enumType.CanonicalName}'");
        }

        var result = request.Instruction.Context.ObjectTemporary;
        EmitManagedFormat(
            request,
            code,
            entry,
            request.Local(0, CliValueKind.ManagedAddress),
            0,
            GetFormatLocal(request),
            result);
        Get(code, result);
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private void EmitManagedFormat(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        EnumMetadataLayout entry,
        int value,
        int payloadOffset,
        int? format,
        int result)
    {
        var underlying = entry.UnderlyingType;
        addresses.Emit(code, entry.Address);
        Get(code, value);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            payloadOffset,
            underlying,
            values.GetValueLayout(underlying).Size);
        if (underlying.StackKind != CliValueKind.I8)
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        if (format is null)
            addresses.Emit(code, 0);
        else
            Get(code, format.Value);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)request.FunctionIndices.Resolve(
                _managedFormat.Value))));
        Set(code, result);
    }

    private static EntityKey ResolveManagedFormat(
        ITypeDescriptorSource descriptors,
        ITypeRepository types,
        IMethodRepository methods)
    {
        var algorithms = descriptors.TypeDescriptors
            .Select(descriptor => types.GetTypeDefinition(descriptor.Type))
            .Single(type => type.FullName == "System.EnumAlgorithms");
        return algorithms.Methods
            .Select(methods.GetMethod)
            .Single(method =>
                method.Name == "Format" &&
                method.IsStatic &&
                method.Signature.ParameterSignatureTypes.Length == 3 &&
                method.Signature.ParameterSignatureTypes[0].StackKind ==
                    CliValueKind.NativeInt)
            .Key;
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

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
