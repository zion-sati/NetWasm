using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeRelationshipClassifier(
    ITypeFinder types,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver identities,
    IImplementedInterfaceResolver interfaces,
    IBaseTypeResolver baseTypes) : ITypeRelationshipClassifier
{
    private readonly Dictionary<(CliTypeIdentity Candidate, CliTypeIdentity Target),
        TypeRelationship> _relationships = [];
    private readonly Dictionary<CliTypeIdentity, IReadOnlySet<CliTypeIdentity>> _interfaceClosures = [];

    public TypeRelationship Classify(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        var key = (candidate, target);
        if (_relationships.TryGetValue(key, out var relationship))
        {
            return relationship;
        }

        var arrayInterface = IsSzArrayGenericInterface(candidate, target);
        relationship = new TypeRelationship(
            (arrayInterface
                ? TypeRelationshipCharacteristics.SzArrayGenericInterface
                : TypeRelationshipCharacteristics.None) |
            (IsHierarchyAssignable(candidate, target)
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None) |
            (IsVariantCompatible(candidate, target)
                ? TypeRelationshipCharacteristics.GenericVariance
                : TypeRelationshipCharacteristics.None));
        _relationships.Add(key, relationship);
        return relationship;
    }

    private bool IsHierarchyAssignable(CliTypeIdentity candidate, CliTypeIdentity target)
    {
        // Array identities are constructed types rather than metadata type
        // definitions. Resolve exact array compatibility before asking the
        // definition resolver for the target; this also covers arrays whose
        // element is a primitive identity (for example, int[]).
        if (target.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            return AreArraysAssignable(candidate, target);
        }

        if (target.CanonicalName == "primitive:object" && IsManagedReference(candidate))
        {
            return true;
        }

        var targetDefinition = typeDefinitions.ResolveTypeIdentity(target);
        if (targetDefinition.IsInterface)
        {
            return ImplementsInterface(candidate, target);
        }

        for (var current = candidate;
             current is not null;
             current = baseTypes.Resolve(current))
        {
            if (SameType(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private bool AreArraysAssignable(CliTypeIdentity candidate, CliTypeIdentity target)
    {
        if (!AreArrayShapesCompatible(candidate, target)
            || GetArrayRank(candidate) != GetArrayRank(target)
            || candidate.ElementType is null
            || target.ElementType is null)
        {
            return false;
        }

        return AreArrayElementsCompatible(candidate.ElementType, target.ElementType);
    }

    private static int GetArrayRank(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray ? 1 : type.ArrayRank;

    private static bool AreArrayShapesCompatible(
        CliTypeIdentity candidate,
        CliTypeIdentity target) =>
        candidate.Shape == target.Shape ||
        candidate.Shape == CliTypeShape.SzArray &&
        target.Shape == CliTypeShape.Array &&
        target.ArrayRank == 1;

    private bool AreArrayElementsCompatible(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        if (IsManagedReference(candidate) && IsManagedReference(target))
        {
            return Classify(candidate, target).IsAssignmentCompatible;
        }

        if (SameType(candidate, target))
        {
            return true;
        }

        var candidateReducedType = GetReducedArrayElementType(candidate);
        return candidateReducedType is not null &&
               candidateReducedType == GetReducedArrayElementType(target);
    }

    private static string? GetReducedArrayElementType(CliTypeIdentity type)
    {
        var storage = type.StackStorageType ?? type;
        return storage.CanonicalName switch
        {
            "primitive:i1" or "primitive:u1" => "i1/u1",
            "primitive:i2" or "primitive:u2" => "i2/u2",
            "primitive:i4" or "primitive:u4" => "i4/u4",
            "primitive:i8" or "primitive:u8" => "i8/u8",
            "primitive:nativeint" or "primitive:nativeuint" => "nativeint/nativeuint",
            _ => null,
        };
    }

    private static bool IsManagedReference(CliTypeIdentity type)
    {
        return !type.IsValueType
            && type.Shape is not (CliTypeShape.GenericTypeParameter
                or CliTypeShape.GenericMethodParameter
                or CliTypeShape.ManagedByReference
                or CliTypeShape.UnmanagedPointer);
    }

    private bool SameType(CliTypeIdentity left, CliTypeIdentity right)
    {
        if (left.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter
            || right.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter)
        {
            return left.Shape == right.Shape
                && left.GenericParameterIndex == right.GenericParameterIndex;
        }

        if (left.Shape is CliTypeShape.SzArray or CliTypeShape.Array ||
            right.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            return left.Shape == right.Shape &&
                   left.ArrayRank == right.ArrayRank &&
                   SameType(left.ElementType!, right.ElementType!);
        }

        if (left.Equals(right))
        {
            return true;
        }

        if (left.Shape == CliTypeShape.Primitive ||
            right.Shape == CliTypeShape.Primitive)
        {
            return false;
        }

        if (left.Shape == CliTypeShape.GenericInstantiation ||
            right.Shape == CliTypeShape.GenericInstantiation)
        {
            return left.Shape == right.Shape &&
                   left.TypeArguments.Length == right.TypeArguments.Length &&
                   left.TypeArguments.Zip(right.TypeArguments)
                       .All(pair => SameType(pair.First, pair.Second)) &&
                   typeDefinitions.ResolveTypeIdentity(left).Key ==
                       typeDefinitions.ResolveTypeIdentity(right).Key;
        }

        return typeDefinitions.ResolveTypeIdentity(left).Key ==
               typeDefinitions.ResolveTypeIdentity(right).Key;
    }

    private bool ImplementsInterface(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        if (candidate.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            candidate = identities.GetTypeIdentity(
                types.FindType("System.Array").Key);
        }

        var interfaceClosure = GetInterfaceClosure(candidate);
        if (interfaceClosure.Contains(target))
        {
            return true;
        }

        foreach (var implementedInterface in interfaceClosure)
        {
            if (IsVariantCompatible(implementedInterface, target))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlySet<CliTypeIdentity> GetInterfaceClosure(CliTypeIdentity candidate)
    {
        if (_interfaceClosures.TryGetValue(candidate, out var cachedClosure))
        {
            return cachedClosure;
        }

        var closure = new HashSet<CliTypeIdentity>();
        var visitedTypes = new HashSet<CliTypeIdentity> { candidate };
        var pendingTypes = new Stack<CliTypeIdentity>();
        pendingTypes.Push(candidate);

        while (pendingTypes.TryPop(out var current))
        {
            foreach (var implementedInterface in interfaces.GetInterfaces(current))
            {
                closure.Add(implementedInterface);
                if (visitedTypes.Add(implementedInterface))
                {
                    pendingTypes.Push(implementedInterface);
                }
            }

            if (baseTypes.Resolve(current) is { } baseType && visitedTypes.Add(baseType))
            {
                pendingTypes.Push(baseType);
            }
        }

        _interfaceClosures.Add(candidate, closure);
        return closure;
    }

    private bool IsVariantCompatible(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        if (SameType(candidate, target))
        {
            return true;
        }

        if (candidate.Shape != CliTypeShape.GenericInstantiation ||
            target.Shape != CliTypeShape.GenericInstantiation ||
            !SameType(candidate.ElementType!, target.ElementType!) ||
            candidate.TypeArguments.Length != target.TypeArguments.Length)
        {
            return false;
        }

        var definition = typeDefinitions.ResolveTypeIdentity(candidate);
        if (definition.GenericParameterVariances.Length != candidate.TypeArguments.Length)
        {
            return false;
        }

        for (var index = 0; index < candidate.TypeArguments.Length; index++)
        {
            var source = candidate.TypeArguments[index];
            var destination = target.TypeArguments[index];
            if (SameType(source, destination))
            {
                continue;
            }
            if (source.IsValueType || destination.IsValueType)
            {
                return false;
            }
            var compatible = definition.GenericParameterVariances[index] switch
            {
                CliGenericVariance.Covariant =>
                    Classify(source, destination).IsAssignmentCompatible,
                CliGenericVariance.Contravariant =>
                    Classify(destination, source).IsAssignmentCompatible,
                _ => false,
            };
            if (!compatible)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsSzArrayGenericInterface(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        if (candidate.Shape != CliTypeShape.SzArray ||
            target.Shape != CliTypeShape.GenericInstantiation ||
            target.TypeArguments.Length != 1 ||
            !AreArrayElementsCompatible(candidate.ElementType!, target.TypeArguments[0]))
        {
            return false;
        }

        return typeDefinitions.ResolveTypeIdentity(target).FullName is
            "System.Collections.Generic.IEnumerable`1" or
            "System.Collections.Generic.ICollection`1" or
            "System.Collections.Generic.IList`1" or
            "System.Collections.Generic.IReadOnlyCollection`1" or
            "System.Collections.Generic.IReadOnlyList`1";
    }
}
