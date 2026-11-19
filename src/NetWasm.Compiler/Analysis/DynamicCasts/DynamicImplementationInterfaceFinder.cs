// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.DynamicCasts;

// The implementation interfaces ([DynamicInterfaceCastableImplementation]) an interface call may dispatch through for
// an object whose class does not implement the call's interface: each interface of the compilation that carries the
// attribute and derives from it, a generic one instantiated with the arguments the call's interface fixes. The
// attributed interfaces are read once, from the whole compilation, so the set does not depend on how much of the
// program a reduction round has reached; the run-time validation of the interface an object returns is the helper's.
internal sealed class DynamicImplementationInterfaceFinder(
    MetadataCompilationSnapshot metadata,
    ITypeIdentityResolver typeIdentities,
    IImplementedInterfaceResolver implementedInterfaces,
    ICustomAttributeDescriptorReader attributes,
    ITypeRelationshipClassifier relationships) : IDynamicImplementationInterfaceFinder
{
    internal const string ImplementationAttribute = "System.Runtime.InteropServices.DynamicInterfaceCastableImplementationAttribute";

    private readonly MetadataCompilationSnapshot _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ?? throw new ArgumentNullException(nameof(typeIdentities));
    private readonly IImplementedInterfaceResolver _implementedInterfaces = implementedInterfaces ??
        throw new ArgumentNullException(nameof(implementedInterfaces));
    private readonly ICustomAttributeDescriptorReader _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    private readonly ITypeRelationshipClassifier _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
    private ImmutableArray<(CliTypeIdentity Definition, int Arity)>? _attributed;

    public ImmutableArray<CliTypeIdentity> Find(CliTypeIdentity interfaceType)
    {
        ArgumentNullException.ThrowIfNull(interfaceType);
        var found = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        foreach (var (definition, arity) in Attributed())
        {
            var candidate = arity == 0 ? definition : Instantiate(definition, arity, interfaceType);
            if (candidate is not null && !found.Contains(candidate) &&
                _relationships.Classify(candidate, interfaceType).IsAssignmentCompatible)
            {
                found.Add(candidate);
            }
        }
        return found.ToImmutable();
    }

    private ImmutableArray<(CliTypeIdentity Definition, int Arity)> Attributed() => _attributed ??= [..
        _metadata.Types
            .Where(type => type.IsInterface)
            .Select(type => (Definition: _typeIdentities.GetTypeIdentity(type.Key), Arity: type.GenericArity))
            .Where(type => _attributes.Read(type.Definition).Any(attribute =>
                attribute.AttributeType.FullName == ImplementationAttribute))
            .OrderBy(type => type.Definition.CanonicalName, StringComparer.Ordinal)];

    // The instantiation of a generic implementation interface whose implemented interface becomes the call's
    // interface: J<T> : I<T> on a call through I<int> is J<int>.
    private CliTypeIdentity? Instantiate(CliTypeIdentity definition, int arity, CliTypeIdentity interfaceType)
    {
        var open = CliTypeIdentity.GenericInstantiation(definition,
            [.. Enumerable.Range(0, arity).Select(index => CliTypeIdentity.GenericParameter(method: false, index))]);
        foreach (var implemented in _implementedInterfaces.GetInterfaces(open))
        {
            var bindings = new CliTypeIdentity?[arity];
            if (Unify(implemented, interfaceType, bindings) && bindings.All(binding => binding is not null))
            {
                return CliTypeIdentity.GenericInstantiation(definition, [.. bindings.Select(binding => binding!)]);
            }
        }
        return null;
    }

    // Binds the implementation interface's own type parameters so that an interface it implements, the pattern, is the
    // call's interface. The pattern names only those parameters, closed types, and instantiations or arrays of them (a
    // pointer or a by-reference is no type argument), and two instantiations of one definition have as many arguments.
    private static bool Unify(CliTypeIdentity pattern, CliTypeIdentity concrete, CliTypeIdentity?[] bindings)
    {
        if (pattern.Shape == CliTypeShape.GenericTypeParameter)
        {
            var index = pattern.GenericParameterIndex;
            if (bindings[index] is { } bound) return bound.Equals(concrete);
            bindings[index] = concrete;
            return true;
        }
        if (!pattern.ContainsGenericParameters) return pattern.Equals(concrete);
        if (pattern.Shape != concrete.Shape) return false;
        return pattern.Shape == CliTypeShape.GenericInstantiation
            ? pattern.ElementType!.Equals(concrete.ElementType) &&
                pattern.TypeArguments.Zip(concrete.TypeArguments).All(pair => Unify(pair.First, pair.Second, bindings))
            : pattern.ArrayRank == concrete.ArrayRank && Unify(pattern.ElementType!, concrete.ElementType!, bindings);
    }
}
