using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class SignatureTypeComparer : ISignatureTypeComparer
{
    public bool Compare(CliTypeIdentity left, CliTypeIdentity right)
    {
        if (left.Equals(right))
        {
            return true;
        }

        return (left.Shape, right.Shape) switch
        {
            (CliTypeShape.Named, CliTypeShape.Named) => string.Equals(
                left.FullName,
                right.FullName,
                System.StringComparison.Ordinal),
            (CliTypeShape.SzArray, CliTypeShape.SzArray) or
            (CliTypeShape.ManagedByReference, CliTypeShape.ManagedByReference) or
            (CliTypeShape.UnmanagedPointer, CliTypeShape.UnmanagedPointer) =>
                Compare(left.ElementType!, right.ElementType!),
            (CliTypeShape.Array, CliTypeShape.Array) =>
                left.ArrayRank == right.ArrayRank &&
                Compare(left.ElementType!, right.ElementType!),
            (CliTypeShape.GenericInstantiation, CliTypeShape.GenericInstantiation) =>
                Compare(left.ElementType!, right.ElementType!) &&
                left.TypeArguments.Length == right.TypeArguments.Length &&
                left.TypeArguments.Zip(right.TypeArguments).All(pair =>
                    Compare(pair.First, pair.Second)),
            _ => false,
        };
    }
}
