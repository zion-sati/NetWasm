// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
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
    IBaseTypeResolver baseTypes,
    bool remembersClassifiedPairs = true) : ITypeRelationshipClassifier
{
    private readonly TypePairMemo _relationships = new();
    private readonly ConcurrentDictionary<CliTypeIdentity, IReadOnlySet<CliTypeIdentity>> _interfaceClosures = [];

    public TypeRelationship Classify(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        // Assignability metadata classifies each runtime type against its targets once: remembering those pairs held
        // most of the layout phase's heap on Devolutions' Avalonia sample (over a hundred million of them), and none
        // was asked twice. Its classifier remembers only the argument and element pairs classified on the way, which
        // recur (Remembered).
        return remembersClassifiedPairs
            ? Remembered(candidate, target)
            : _relationships.TryGet(candidate, target, out var characteristics)
                ? new((TypeRelationshipCharacteristics)characteristics)
                : Compute(candidate, target);
    }

    private TypeRelationship Remembered(CliTypeIdentity candidate, CliTypeIdentity target)
    {
        if (_relationships.TryGet(candidate, target, out var characteristics))
        {
            return new((TypeRelationshipCharacteristics)characteristics);
        }
        var relationship = Compute(candidate, target);
        _relationships.Add(candidate, target, (byte)relationship.Characteristics);
        return relationship;
    }

    private TypeRelationship Compute(CliTypeIdentity candidate, CliTypeIdentity target)
    {
        var arrayInterface = IsSzArrayGenericInterface(candidate, target);
        var hierarchy = IsHierarchyAssignable(candidate, target, out var throughVariance);
        if (!arrayInterface && !hierarchy && SharedArrayShapes.IsShared(candidate))
        {
            return new TypeRelationship(SharedElementChecked(candidate, target));
        }
        return new TypeRelationship(
            (arrayInterface
                ? TypeRelationshipCharacteristics.SzArrayGenericInterface
                : TypeRelationshipCharacteristics.None) |
            (hierarchy
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None) |
            (throughVariance
                ? TypeRelationshipCharacteristics.GenericVariance
                : TypeRelationshipCharacteristics.None));
    }

    // Whether the candidate is assignable to the target through the CLI type hierarchy: the same type, an
    // array of compatible elements, a base type, an implemented interface, or a variant instantiation of
    // any of those (IEnumerator<string> to IEnumerator<object>, Func<string> to Func<object>, a class
    // implementing IEnumerable<string> to IEnumerable<object>). throughVariance says the match needed
    // generic variance, so an interface slot's signature differs from its implementation's.
    private bool IsHierarchyAssignable(CliTypeIdentity candidate, CliTypeIdentity target, out bool throughVariance)
    {
        throughVariance = false;
        if (SameType(candidate, target))
        {
            return true;
        }

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
            return ImplementsInterface(candidate, target, out throughVariance);
        }

        for (var current = candidate;
             current is not null;
             current = baseTypes.Resolve(current))
        {
            if (SameType(current, target))
            {
                return true;
            }
            if (IsVariantCompatible(current, target))
            {
                throughVariance = true;
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
            return Remembered(candidate, target).IsAssignmentCompatible;
        }

        if (SameType(candidate, target))
        {
            return true;
        }

        var candidateReducedType = GetReducedArrayElementType(candidate);
        return candidateReducedType is not null &&
               candidateReducedType == GetReducedArrayElementType(target);
    }

    // The reduced type an array of the value type converts through: arrays whose elements reduce to the same one
    // convert to each other (an enum through its underlying type); a value type with none converts only to itself.
    internal static string? GetReducedArrayElementType(CliTypeIdentity type)
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

        if (left.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.ManagedByReference ||
            right.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.ManagedByReference)
        {
            return left.Shape == right.Shape && SameType(left.ElementType!, right.ElementType!);
        }
        if (left.Shape == CliTypeShape.FunctionPointer || right.Shape == CliTypeShape.FunctionPointer)
        {
            return false; // Equal signatures already matched structural identity above.
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
        CliTypeIdentity target,
        out bool throughVariance)
    {
        throughVariance = false;
        if (candidate.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            candidate = identities.GetTypeIdentity(
                types.FindType("System.Array").Key);
        }
        else if (IsVariantCompatible(candidate, target))
        {
            // The candidate is itself a variant instantiation of the target interface.
            throughVariance = true;
            return true;
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
                throughVariance = true;
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

        return _interfaceClosures.GetOrAdd(candidate, closure);
    }

    // Callers have already ruled out the same type, so identical type arguments only pass here when an
    // interface in a closure is the target under another identity.
    private bool IsVariantCompatible(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
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
                    Remembered(source, destination).IsAssignmentCompatible,
                CliGenericVariance.Contravariant =>
                    Remembered(destination, source).IsAssignmentCompatible,
                _ => false,
            };
            if (!compatible)
            {
                return false;
            }
        }

        return true;
    }

    // A shared array holds any reference element in its shape, so beyond what every array of the shape converts to, it
    // may convert to an array of the shape with a reference element, and a vector to one of the generic interfaces
    // arrays implement over a reference type: where the element in its header converts to that element or argument.
    private TypeRelationshipCharacteristics SharedElementChecked(CliTypeIdentity candidate, CliTypeIdentity target)
    {
        if (target.ContainsGenericParameters)
        {
            return TypeRelationshipCharacteristics.None;
        }
        if (target.Shape == candidate.Shape && GetArrayRank(target) == GetArrayRank(candidate) &&
            IsSharedElement(target.ElementType!))
        {
            return TypeRelationshipCharacteristics.ElementChecked;
        }
        return candidate.Shape == CliTypeShape.SzArray && target.Shape == CliTypeShape.GenericInstantiation &&
            target.TypeArguments.Length == 1 && IsSharedElement(target.TypeArguments[0]) && IsArrayInterface(target)
                ? TypeRelationshipCharacteristics.ElementChecked | TypeRelationshipCharacteristics.SzArrayGenericInterface
                : TypeRelationshipCharacteristics.None;
    }

    // An element a shared array can hold: a reference type that is not the shared arrays' own element.
    private static bool IsSharedElement(CliTypeIdentity type) =>
        IsManagedReference(type) && type.Shape != CliTypeShape.FunctionPointer && !SharedArrayShapes.IsElement(type);

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

        return IsArrayInterface(target);
    }

    // IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyCollection<T> or IReadOnlyList<T>, which every vector implements.
    private bool IsArrayInterface(CliTypeIdentity target) =>
        typeDefinitions.ResolveTypeIdentity(target).FullName is
            "System.Collections.Generic.IEnumerable`1" or
            "System.Collections.Generic.ICollection`1" or
            "System.Collections.Generic.IList`1" or
            "System.Collections.Generic.IReadOnlyCollection`1" or
            "System.Collections.Generic.IReadOnlyList`1";
}
