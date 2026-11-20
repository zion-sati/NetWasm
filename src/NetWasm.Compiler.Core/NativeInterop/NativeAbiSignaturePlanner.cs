// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed class NativeAbiSignaturePlanner(INativeAggregateAbiPlanner aggregates) : INativeAbiSignaturePlanner
{
    private readonly INativeAggregateAbiPlanner _aggregates =
        aggregates ?? throw new ArgumentNullException(nameof(aggregates));

    public NativeAbiSignaturePlan Plan(
        MethodSignatureModel signature,
        NativeAbiSignatureKind kind,
        string methodName,
        NativeMarshallingRules rules = NativeMarshallingRules.Runtime)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (kind is not (NativeAbiSignatureKind.Import or NativeAbiSignatureKind.Callback))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var result = PlanType(signature.ReturnSignatureType, kind, methodName, rules, isReturn: true);
        var physicalParameters = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var parameters = ImmutableArray.CreateBuilder<NativeAbiParameterPlan>();
        int? hiddenResult = null;
        if (result.Kind == NativeAbiValueKind.IndirectAggregate)
        {
            hiddenResult = 0;
            physicalParameters.Add(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        }
        for (var index = 0; index < signature.ParameterSignatureTypes.Length; index++)
        {
            var value = PlanType(signature.ParameterSignatureTypes[index], kind, methodName, rules, isReturn: false);
            int? physicalIndex = null;
            if (value.Kind != NativeAbiValueKind.IgnoredAggregate)
            {
                physicalIndex = physicalParameters.Count;
                physicalParameters.Add(value.PhysicalType!);
            }
            parameters.Add(new(index, physicalIndex, value));
        }
        var physicalResult = result.Kind is NativeAbiValueKind.Void or
            NativeAbiValueKind.IgnoredAggregate or NativeAbiValueKind.IndirectAggregate
            ? CliTypeIdentity.FromStackKind(CliValueKind.Void) : result.PhysicalType!;
        return new(signature, new(physicalResult, physicalParameters.ToImmutable()),
            parameters.ToImmutable(), result, hiddenResult);
    }

    private NativeAbiValuePlan PlanType(
        CliTypeIdentity type,
        NativeAbiSignatureKind kind,
        string methodName,
        NativeMarshallingRules rules,
        bool isReturn)
    {
        if (type.Shape == CliTypeShape.UnmanagedPointer && !type.ContainsGenericParameters)
            return new(NativeAbiValueKind.Scalar, type, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));

        // A ref, in or out of a blittable value pins its storage and passes its address, as desktop .NET's runtime
        // marshalling does: a scalar, a pointer, an unmanaged function pointer or a struct the C ABI lays out. Under
        // disabled runtime marshalling desktop .NET rejects it.
        if (!isReturn && kind == NativeAbiSignatureKind.Import && rules == NativeMarshallingRules.Runtime &&
            type.Shape == CliTypeShape.ManagedByReference && PinsByReference(type.ElementType!, methodName, rules))
            return new(NativeAbiValueKind.Scalar, type, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));

        if (!isReturn && kind == NativeAbiSignatureKind.Import &&
            type.Shape == CliTypeShape.FunctionPointer)
        {
            ValidateFunctionPointer(type, methodName, rules);
            return new(
                NativeAbiValueKind.Scalar,
                type,
                CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        }

        if (type.Shape == CliTypeShape.Primitive &&
            (NativeAbiScalars.IsBlittable(type, rules) || isReturn && type.StackKind == CliValueKind.Void))
            return new(type.StackKind == CliValueKind.Void ? NativeAbiValueKind.Void : NativeAbiValueKind.Scalar,
                type, type.StackKind == CliValueKind.Void ? null : type);

        // A struct by value, as a parameter or result, of an import or of a callback the C ABI calls.
        if (type.IsValueType && type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation)
            return _aggregates.Plan(type, methodName, rules);

        throw new CompilerException(new(DiagnosticCode.NativeInterop,
            "The native ABI requires qualified scalars, pointers or blittable aggregate layouts; scalar ref/out is supported only for imports.",
            methodName));
    }

    private bool PinsByReference(CliTypeIdentity element, string methodName, NativeMarshallingRules rules)
    {
        if (element.ContainsGenericParameters)
            return false;
        switch (element.Shape)
        {
            case CliTypeShape.Primitive:
                return NativeAbiScalars.IsBlittable(element);
            case CliTypeShape.UnmanagedPointer:
                return true;
            case CliTypeShape.FunctionPointer:
                ValidateFunctionPointer(element, methodName, rules);
                return true;
            case CliTypeShape.Named or CliTypeShape.GenericInstantiation when element.IsValueType:
                // The aggregate planner rejects a struct whose layout is not the C ABI's.
                _aggregates.Plan(element, methodName, rules);
                return true;
            default:
                return false;
        }
    }

    private void ValidateFunctionPointer(
        CliTypeIdentity type,
        string methodName,
        NativeMarshallingRules rules)
    {
        var pointer = type.FunctionPointerSignature!;
        if (type.ContainsGenericParameters || pointer.GenericArity != 0 ||
            pointer.RequiredParameterCount !=
            pointer.Signature.ParameterSignatureTypes.Length ||
            !UnmanagedCallingConventions.IsUnmanagedHeader(pointer.Header))
        {
            throw UnsupportedFunctionPointer(methodName);
        }

        // The C ABI reads no modifier; an optional one names a CallConv type.
        var returnType = pointer.Signature.ReturnSignatureType;
        var conventions = new List<string?>();
        while (returnType.Shape == CliTypeShape.Modified)
        {
            if (!returnType.IsRequiredModifier)
            {
                conventions.Add(returnType.CustomModifier!.FullName);
            }
            returnType = returnType.ElementType!;
        }
        if (pointer.Header == UnmanagedCallingConventions.UnmanagedHeader &&
            UnmanagedCallingConventions.Unsupported(conventions) is not null)
        {
            throw UnsupportedFunctionPointer(methodName);
        }

        Plan(
            new(
                returnType,
                pointer.Signature.ParameterSignatureTypes),
            NativeAbiSignatureKind.Callback,
            methodName,
            rules);
    }

    private static CompilerException UnsupportedFunctionPointer(string methodName) =>
        new(new(
            DiagnosticCode.NativeInterop,
            "Native function-pointer parameters require a closed, fixed-arity scalar callback signature the C ABI calls: unmanaged, Cdecl, Stdcall or Thiscall.",
            methodName));
}
