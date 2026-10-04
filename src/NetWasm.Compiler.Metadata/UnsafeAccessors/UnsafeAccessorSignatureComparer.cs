using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

/// <summary>Compares typical signatures before substituting the accessor's concrete arguments.</summary>
internal sealed class UnsafeAccessorSignatureComparer(ITypeDefinitionResolver definitions)
    : IUnsafeAccessorSignatureComparer
{
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public bool Compare(CliTypeIdentity left, CliTypeIdentity right, bool includeModifiers)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!includeModifiers)
        {
            while (left.Shape == CliTypeShape.Modified)
                left = left.ElementType!;
            while (right.Shape == CliTypeShape.Modified)
                right = right.ElementType!;
        }

        if (left.Shape != right.Shape || left.IsValueType != right.IsValueType)
            return false;

        return left.Shape switch
        {
            CliTypeShape.Named => _definitions.ResolveTypeIdentity(left).Key == _definitions.ResolveTypeIdentity(right).Key,
            CliTypeShape.SzArray or CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer =>
                Compare(left.ElementType!, right.ElementType!, includeModifiers),
            CliTypeShape.Array => left.ArrayRank == right.ArrayRank &&
                Compare(left.ElementType!, right.ElementType!, includeModifiers),
            CliTypeShape.GenericInstantiation =>
                Compare(left.ElementType!, right.ElementType!, includeModifiers) &&
                CompareTypes(left.TypeArguments, right.TypeArguments, includeModifiers),
            CliTypeShape.Modified => left.IsRequiredModifier == right.IsRequiredModifier &&
                Compare(left.CustomModifier!, right.CustomModifier!, includeModifiers) &&
                Compare(left.ElementType!, right.ElementType!, includeModifiers),
            CliTypeShape.FunctionPointer => CompareFunctionPointers(
                left.FunctionPointerSignature!, right.FunctionPointerSignature!, includeModifiers),
            _ => left.Equals(right),
        };
    }

    private bool CompareTypes(ImmutableArray<CliTypeIdentity> left, ImmutableArray<CliTypeIdentity> right, bool includeModifiers)
    {
        if (left.Length != right.Length)
            return false;
        for (var index = 0; index < left.Length; index++)
        {
            if (!Compare(left[index], right[index], includeModifiers))
                return false;
        }

        return true;
    }

    private bool CompareFunctionPointers(CliFunctionPointerSignature left, CliFunctionPointerSignature right, bool includeModifiers) =>
        left.Header == right.Header && left.GenericArity == right.GenericArity &&
        left.RequiredParameterCount == right.RequiredParameterCount &&
        Compare(left.Signature.ReturnSignatureType, right.Signature.ReturnSignatureType, includeModifiers) &&
        CompareTypes(left.Signature.ParameterSignatureTypes, right.Signature.ParameterSignatureTypes, includeModifiers);
}
