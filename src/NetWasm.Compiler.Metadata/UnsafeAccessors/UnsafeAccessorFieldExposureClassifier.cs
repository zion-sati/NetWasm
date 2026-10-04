using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

// Strategy: identify declarations that may expose a field address without an
// ldflda in a physical body. This proves absence of exposure, not reachability.
public sealed class UnsafeAccessorFieldExposureClassifier : IUnsafeAccessorFieldExposureClassifier
{
    public bool Classify(MethodDefinitionModel method, FieldInstanceModel field)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(field);
        var declaration = method.UnsafeAccessor;
        if (declaration is null || method.HasBody || !method.IsStatic || declaration.IsMalformed ||
            declaration.Kind is not ((int)UnsafeAccessorMemberKind.Field or (int)UnsafeAccessorMemberKind.StaticField) ||
            field.Definition.IsStatic != (declaration.Kind == (int)UnsafeAccessorMemberKind.StaticField))
        {
            return false;
        }
        var name = declaration.NameSpecified ? declaration.Name : method.Name;
        if (!StringComparer.Ordinal.Equals(name, field.Definition.Name) ||
            method.Signature.ParameterSignatureTypes.Length != 1)
        {
            return false;
        }
        var result = Unmodified(method.Signature.ReturnSignatureType);
        if (result.Shape != CliTypeShape.ManagedByReference)
        {
            return false;
        }
        // The descriptor intentionally does not resolve translated parameter
        // types. An unknown translated owner must not preserve a readonly proof.
        if (declaration.HasTypeTranslation)
        {
            return true;
        }
        if (!Unmodified(result.ElementType!).Equals(Unmodified(field.Definition.SignatureType)))
        {
            return false;
        }
        var owner = Unmodified(method.Signature.ParameterSignatureTypes[0]);
        if (owner.Shape == CliTypeShape.ManagedByReference)
        {
            owner = Unmodified(owner.ElementType!);
        }
        var wanted = Unmodified(field.DeclaringType);
        // Proofs are cached by field definition. Exposure of any instantiation
        // invalidates that definition's proof, regardless of which closes first.
        if (owner.Shape == CliTypeShape.GenericInstantiation)
        {
            owner = Unmodified(owner.ElementType!);
        }
        if (wanted.Shape == CliTypeShape.GenericInstantiation)
        {
            wanted = Unmodified(wanted.ElementType!);
        }
        return owner.Equals(wanted);
    }

    private static CliTypeIdentity Unmodified(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified)
        {
            type = type.ElementType!;
        }
        return type;
    }
}
