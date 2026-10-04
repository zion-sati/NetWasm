using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

// Strategy: validate the declaration's typical generic variables against the
// target's constraints, before specializing either with concrete call arguments.
// Mirrors runtime v10.0.0 unsafeaccessors.cpp's constraint verification and
// typedesc.cpp's generic-variable constraint satisfaction, rather than comparing
// constraint lists. Stronger and reordered declarations remain compatible.
internal sealed class UnsafeAccessorConstraintValidator(
    IUnsafeAccessorSignatureComparer signatures,
    IUnsafeAccessorTypeAssignability assignability,
    ITypeDefinitionResolver definitions,
    IMethodRepository methods,
    CliTypeIdentity objectType,
    CliTypeIdentity valueType) : IUnsafeAccessorConstraintValidator
{
    public bool Validate(UnsafeAccessorGenericConstraints accessor, UnsafeAccessorGenericConstraints target)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(target);
        return ValidateGroup(target.TypeParameters, accessor.TypeParameters, false) &&
            ValidateGroup(target.MethodParameters, accessor.MethodParameters, true);

        bool ValidateGroup(
            ImmutableArray<UnsafeAccessorGenericParameterConstraints> required,
            ImmutableArray<UnsafeAccessorGenericParameterConstraints> declared,
            bool method)
        {
            // The runtime only compares groups present on the target. Method
            // signature matching independently enforces generic method arity.
            if (required.IsEmpty)
                return true;
            if (required.Length != declared.Length)
                return false;
            for (var index = 0; index < required.Length; index++)
            {
                var variable = CliTypeIdentity.GenericParameter(method, index);
                var parameter = required[index];
                foreach (var special in SpecialConstraints)
                {
                    if ((parameter.Attributes & special) != 0 &&
                        !SatisfiesSpecial(accessor, variable, special, true, []))
                        return false;
                }
                var known = new HashSet<CliTypeIdentity>();
                Gather(accessor, variable, known);
                foreach (var constraint in parameter.Types)
                {
                    if (!Same(constraint, objectType) && !SatisfiesGeneral(accessor, known, constraint))
                        return false;
                }
            }
            return true;
        }
    }

    private static readonly GenericParameterAttributes[] SpecialConstraints =
    [
        GenericParameterAttributes.ReferenceTypeConstraint,
        GenericParameterAttributes.NotNullableValueTypeConstraint,
        GenericParameterAttributes.DefaultConstructorConstraint,
    ];

    private bool SatisfiesSpecial(
        UnsafeAccessorGenericConstraints accessor,
        CliTypeIdentity variable,
        GenericParameterAttributes required,
        bool initial,
        HashSet<CliTypeIdentity> visited)
    {
        if (!visited.Add(variable) || !TryGetParameter(accessor, variable, out var parameter))
            return false;
        var flags = parameter.Attributes;
        if (required == GenericParameterAttributes.ReferenceTypeConstraint
            ? initial && (flags & GenericParameterAttributes.ReferenceTypeConstraint) != 0
            : (flags & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 ||
                initial && required == GenericParameterAttributes.DefaultConstructorConstraint &&
                (flags & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            return true;

        foreach (var constraint in parameter.Types)
        {
            if (IsVariable(constraint))
            {
                if (SatisfiesSpecial(accessor, constraint, required, false, visited))
                    return true;
                continue;
            }
            var definition = definitions.ResolveTypeIdentity(constraint);
            if (definition.IsInterface)
                continue;
            if (required == GenericParameterAttributes.ReferenceTypeConstraint)
            {
                // Object/ValueType/Enum and interfaces do not imply that an
                // eventual argument is a reference type. Other class ancestry does.
                if (!definition.IsValueType && !Same(constraint, objectType) &&
                    !assignability.IsAssignable(constraint, valueType, accessor))
                    return true;
            }
            else if (definition.IsValueType &&
                (required == GenericParameterAttributes.NotNullableValueTypeConstraint
                    ? !IsNullable(definition)
                    : HasPublicDefaultConstructor(definition)))
            {
                return true;
            }
        }
        return false;
    }

    private bool SatisfiesGeneral(
        UnsafeAccessorGenericConstraints accessor,
        HashSet<CliTypeIdentity> known,
        CliTypeIdentity required)
    {
        foreach (var candidate in known)
        {
            if (Same(candidate, required))
                return true;
            if (IsVariable(candidate))
            {
                if (Same(required, valueType) && TryGetParameter(accessor, candidate, out var parameter) &&
                    (parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                    return true;
            }
            else if (assignability.IsAssignable(candidate, required, accessor))
            {
                return true;
            }
        }
        return false;
    }

    private static void Gather(UnsafeAccessorGenericConstraints accessor, CliTypeIdentity type, HashSet<CliTypeIdentity> known)
    {
        if (!known.Add(type) || !TryGetParameter(accessor, type, out var parameter))
            return;
        foreach (var constraint in parameter.Types)
            Gather(accessor, constraint, known);
    }

    private bool HasPublicDefaultConstructor(TypeDefinitionModel definition)
    {
        foreach (var key in definition.Methods)
        {
            var method = methods.GetMethod(key);
            if (!method.IsStatic && method.Name == ".ctor" && method.Signature.ParameterTypes.IsEmpty)
                return method.IsPublic;
        }
        return true;
    }

    private bool IsNullable(TypeDefinitionModel definition) =>
        definition.Key.Assembly == definitions.ResolveTypeIdentity(valueType).Key.Assembly &&
        definition.FullName == "System.Nullable`1";

    private bool Same(CliTypeIdentity left, CliTypeIdentity right) => signatures.Compare(left, right, false);

    private static bool IsVariable(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter;

    private static bool TryGetParameter(
        UnsafeAccessorGenericConstraints constraints,
        CliTypeIdentity type,
        out UnsafeAccessorGenericParameterConstraints parameter)
    {
        var group = type.Shape switch
        {
            CliTypeShape.GenericTypeParameter => constraints.TypeParameters,
            CliTypeShape.GenericMethodParameter => constraints.MethodParameters,
            _ => [],
        };
        if ((uint)type.GenericParameterIndex < (uint)group.Length)
        {
            parameter = group[type.GenericParameterIndex];
            return true;
        }
        parameter = null!;
        return false;
    }
}
