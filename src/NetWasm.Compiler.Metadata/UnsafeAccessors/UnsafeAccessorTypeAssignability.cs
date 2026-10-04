using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

// Strategy: prove nominal constraint implication without substituting concrete
// call arguments for the declaration's typical generic variables.
internal sealed class UnsafeAccessorTypeAssignability(
    IUnsafeAccessorSignatureComparer signatures,
    ITypeDefinitionResolver definitions,
    IBaseTypeIdentityResolver baseTypes,
    IImplementedInterfaceResolver interfaces,
    ITypeFinder types,
    CliTypeIdentity objectType,
    CliTypeIdentity valueType) : IUnsafeAccessorTypeAssignability
{
    public bool IsAssignable(CliTypeIdentity source, CliTypeIdentity target, UnsafeAccessorGenericConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(constraints);
        return Assignable(source, target, []);

        bool Assignable(CliTypeIdentity from, CliTypeIdentity to,
            HashSet<(CliTypeIdentity From, CliTypeIdentity To)> active)
        {
            if (Same(from, to))
                return true;
            if (!active.Add((from, to)))
                return false;
            try
            {
                if (Same(to, objectType))
                    return from.StackKind != CliValueKind.Void && from.Shape is not
                        (CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer);

                if (Parameter(from) is { } parameter)
                {
                    if ((parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 && Same(to, valueType))
                        return true;
                    foreach (var bound in parameter.Types)
                    {
                        if (Assignable(bound, to, active))
                            return true;
                    }
                    return false;
                }

                if (from.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
                {
                    if (to.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
                    {
                        var sameShape = from.Shape == to.Shape ||
                            from.Shape == CliTypeShape.SzArray && to.Shape == CliTypeShape.Array && to.ArrayRank == 1;
                        var fromRank = from.Shape == CliTypeShape.SzArray ? 1 : from.ArrayRank;
                        var toRank = to.Shape == CliTypeShape.SzArray ? 1 : to.ArrayRank;
                        return sameShape && fromRank == toRank && ArrayElement(from.ElementType!, to.ElementType!, active);
                    }

                    if (from.Shape == CliTypeShape.SzArray && to.Shape == CliTypeShape.GenericInstantiation &&
                        to.TypeArguments.Length == 1)
                    {
                        var definition = definitions.ResolveTypeIdentity(to);
                        if (definition.Key.Assembly == definitions.ResolveTypeIdentity(valueType).Key.Assembly &&
                            definition.FullName is "System.Collections.Generic.IEnumerable`1" or
                                "System.Collections.Generic.ICollection`1" or "System.Collections.Generic.IList`1" or
                                "System.Collections.Generic.IReadOnlyCollection`1" or "System.Collections.Generic.IReadOnlyList`1")
                            return ArrayElement(from.ElementType!, to.TypeArguments[0], active);
                    }

                    return Assignable(CliTypeIdentity.FromDefinition(types.FindType("System.Array")), to, active);
                }

                if (from.Shape is not (CliTypeShape.Named or CliTypeShape.Primitive or CliTypeShape.GenericInstantiation))
                    return false;

                if (Variant(from, to, active))
                    return true;
                if (baseTypes.GetBaseTypeIdentity(from) is { } parent && Assignable(parent, to, active))
                    return true;
                foreach (var implemented in interfaces.GetInterfaces(from))
                {
                    if (Assignable(implemented, to, active))
                        return true;
                }
                return false;
            }
            finally
            {
                active.Remove((from, to));
            }
        }

        bool ArrayElement(CliTypeIdentity from, CliTypeIdentity to,
            HashSet<(CliTypeIdentity From, CliTypeIdentity To)> active)
        {
            if (Same(from, to))
                return true;
            if (IsReference(from, true, []) && IsReference(to, true, []))
                return Assignable(from, to, active);
            // CLR array compatibility also permits the matching signed/unsigned
            // storage pair, including enum underlying storage. Keep these rules
            // aligned with ordinary array assignment in TypeRelationshipClassifier.
            var reduced = ReducedArrayElement(from);
            return reduced is not null && reduced == ReducedArrayElement(to);
        }

        bool Variant(CliTypeIdentity from, CliTypeIdentity to,
            HashSet<(CliTypeIdentity From, CliTypeIdentity To)> active)
        {
            if (from.Shape != CliTypeShape.GenericInstantiation || to.Shape != CliTypeShape.GenericInstantiation ||
                !Same(from.ElementType!, to.ElementType!) || from.TypeArguments.Length != to.TypeArguments.Length)
                return false;
            var definition = definitions.ResolveTypeIdentity(from);
            if (definition.GenericParameterVariances.Length != from.TypeArguments.Length)
                throw new BadImageFormatException("Generic variance metadata does not match the type's arity.");

            for (var index = 0; index < from.TypeArguments.Length; index++)
            {
                var left = from.TypeArguments[index];
                var right = to.TypeArguments[index];
                if (Same(left, right))
                    continue;
                if (!IsReference(left, true, []) || !IsReference(right, true, []))
                    return false;
                var compatible = definition.GenericParameterVariances[index] switch
                {
                    CliGenericVariance.Covariant => Assignable(left, right, active),
                    CliGenericVariance.Contravariant => Assignable(right, left, active),
                    _ => false,
                };
                if (!compatible)
                    return false;
            }
            return true;
        }

        bool IsReference(CliTypeIdentity type, bool initial, HashSet<CliTypeIdentity> active)
        {
            if (!active.Add(type))
                return false;
            if (Parameter(type) is { } parameter)
            {
                if (initial && (parameter.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                    return true;
                foreach (var bound in parameter.Types)
                {
                    if (Parameter(bound) is not null)
                    {
                        if (IsReference(bound, false, active))
                            return true;
                    }
                    else if (bound.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive)
                    {
                        var definition = definitions.ResolveTypeIdentity(bound);
                        if (!definition.IsInterface && !definition.IsValueType && !Same(bound, objectType) &&
                            !Assignable(bound, valueType, []))
                            return true;
                    }
                }
                return false;
            }
            return type.Shape is CliTypeShape.SzArray or CliTypeShape.Array ||
                type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive &&
                !definitions.ResolveTypeIdentity(type).IsValueType;
        }

        UnsafeAccessorGenericParameterConstraints? Parameter(CliTypeIdentity type)
        {
            ImmutableArray<UnsafeAccessorGenericParameterConstraints> group = type.Shape switch
            {
                CliTypeShape.GenericTypeParameter => constraints.TypeParameters,
                CliTypeShape.GenericMethodParameter => constraints.MethodParameters,
                _ => [],
            };
            return (uint)type.GenericParameterIndex < (uint)group.Length ? group[type.GenericParameterIndex] : null;
        }
    }

    private bool Same(CliTypeIdentity left, CliTypeIdentity right) => signatures.Compare(left, right, includeModifiers: false);

    private static string? ReducedArrayElement(CliTypeIdentity type) => (type.StackStorageType ?? type).CanonicalName switch
    {
        "primitive:i1" or "primitive:u1" => "i1/u1",
        "primitive:i2" or "primitive:u2" => "i2/u2",
        "primitive:i4" or "primitive:u4" => "i4/u4",
        "primitive:i8" or "primitive:u8" => "i8/u8",
        "primitive:nativeint" or "primitive:nativeuint" => "nativeint/nativeuint",
        _ => null,
    };
}
