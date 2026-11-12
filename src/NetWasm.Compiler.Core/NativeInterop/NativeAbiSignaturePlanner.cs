using System.Collections.Generic;
using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed class NativeAbiSignaturePlanner(INativeAggregateAbiPlanner aggregates) : INativeAbiSignaturePlanner
{
    // The signature headers of an unmanaged function pointer the C ABI calls: C, StdCall and ThisCall
    // (0x01 to 0x03), and Unmanaged, whose return type's modifiers name its CallConv types.
    private const byte CdeclFunctionPointerHeader = 0x01;
    private const byte ThiscallFunctionPointerHeader = 0x03;
    private const byte UnmanagedFunctionPointerHeader = 0x09;

    private readonly INativeAggregateAbiPlanner _aggregates =
        aggregates ?? throw new ArgumentNullException(nameof(aggregates));

    public NativeAbiSignaturePlan Plan(
        MethodSignatureModel signature,
        NativeAbiSignatureKind kind,
        string methodName)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (kind is not (NativeAbiSignatureKind.Import or NativeAbiSignatureKind.Callback))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var result = PlanType(signature.ReturnSignatureType, kind, methodName, isReturn: true);
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
            var value = PlanType(signature.ParameterSignatureTypes[index], kind, methodName, isReturn: false);
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
        bool isReturn)
    {
        if (type.Shape == CliTypeShape.UnmanagedPointer && !type.ContainsGenericParameters)
            return new(NativeAbiValueKind.Scalar, type, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));

        if (!isReturn && kind == NativeAbiSignatureKind.Import &&
            type.Shape == CliTypeShape.ManagedByReference &&
            type.ElementType!.Shape == CliTypeShape.Primitive && NativeAbiScalars.IsBlittable(type.ElementType))
            return new(NativeAbiValueKind.Scalar, type, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));

        if (!isReturn && kind == NativeAbiSignatureKind.Import &&
            type.Shape == CliTypeShape.FunctionPointer)
        {
            ValidateFunctionPointer(type, methodName);
            return new(
                NativeAbiValueKind.Scalar,
                type,
                CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        }

        if (type.Shape == CliTypeShape.Primitive &&
            (NativeAbiScalars.IsBlittable(type) || isReturn && type.StackKind == CliValueKind.Void))
            return new(type.StackKind == CliValueKind.Void ? NativeAbiValueKind.Void : NativeAbiValueKind.Scalar,
                type, type.StackKind == CliValueKind.Void ? null : type);

        if (kind == NativeAbiSignatureKind.Import && type.IsValueType &&
            type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation)
            return _aggregates.Plan(type, methodName);

        throw new CompilerException(new(DiagnosticCode.NativeInterop,
            "The native ABI requires qualified scalars, pointers or blittable aggregate layouts; scalar ref/out is supported only for imports.",
            methodName));
    }

    private void ValidateFunctionPointer(
        CliTypeIdentity type,
        string methodName)
    {
        var pointer = type.FunctionPointerSignature!;
        if (type.ContainsGenericParameters || pointer.GenericArity != 0 ||
            pointer.RequiredParameterCount !=
            pointer.Signature.ParameterSignatureTypes.Length ||
            pointer.Header is not (>= CdeclFunctionPointerHeader and <= ThiscallFunctionPointerHeader or
                UnmanagedFunctionPointerHeader))
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
        if (pointer.Header == UnmanagedFunctionPointerHeader &&
            UnmanagedCallingConventions.Unsupported(conventions) is not null)
        {
            throw UnsupportedFunctionPointer(methodName);
        }

        Plan(
            new(
                returnType,
                pointer.Signature.ParameterSignatureTypes),
            NativeAbiSignatureKind.Callback,
            methodName);
    }

    private static CompilerException UnsupportedFunctionPointer(string methodName) =>
        new(new(
            DiagnosticCode.NativeInterop,
            "Native function-pointer parameters require a closed, fixed-arity scalar callback signature the C ABI calls: unmanaged, Cdecl, Stdcall or Thiscall.",
            methodName));
}
