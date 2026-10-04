using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed class UnsafeAccessorFieldResolver(
    IFieldRepository fields,
    IUnsafeAccessorSignatureReader signatures,
    IUnsafeAccessorSignatureComparer comparer) : IUnsafeAccessorMemberResolver
{
    public UnsafeAccessorMemberMatch Resolve(UnsafeAccessorMemberRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var returnType = request.Signature.ReturnType;
        while (returnType.Shape == CliTypeShape.Modified)
            returnType = returnType.ElementType!;

        // The declaration boundary validates byref return shape. Unlike methods,
        // the CLR field lookup never checks the accessor's generic constraints.
        foreach (var key in request.Owner.Fields)
        {
            var field = fields.GetField(key);
            if (field.Name == request.Name &&
                field.IsStatic == (request.Kind == UnsafeAccessorMemberKind.StaticField) &&
                !field.IsLiteral &&
                comparer.Compare(returnType.ElementType!, signatures.Read(field), includeModifiers: false))
            {
                return new UnsafeAccessorMemberMatch.Field(field);
            }
        }

        return new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.MissingField);
    }
}
