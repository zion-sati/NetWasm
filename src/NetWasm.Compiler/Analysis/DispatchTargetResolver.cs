using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchTargetResolver(
    ITypeFinder types,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodImplementationResolver methodImplementations,
    IImplementedInterfaceResolver implementedInterfaces,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    ITypeRelationshipClassifier relationships,
    IBaseTypeResolver baseTypes) : IDispatchTargetResolver
{
    private readonly Dictionary<(string Declaration, string Receiver), DispatchTargetModel?> _targets = [];

    public DispatchTargetModel? Resolve(
        MethodInstanceModel declaration,
        CliTypeIdentity receiver)
    {
        var key = (declaration.CanonicalName, receiver.CanonicalName);
        if (_targets.TryGetValue(key, out var target))
        {
            return target;
        }

        target = ResolveUncached(declaration, receiver);
        _targets.Add(key, target);
        return target;
    }

    private DispatchTargetModel? ResolveUncached(
        MethodInstanceModel declaration,
        CliTypeIdentity receiver)
    {
        if (!relationships.Classify(receiver, declaration.DeclaringType).IsAssignmentCompatible)
        {
            return null;
        }

        var implementation = ResolveImplementation(receiver, declaration);
        return implementation is not null && !implementation.Definition.IsAbstract
            ? new DispatchTargetModel(receiver, implementation)
            : null;
    }

    private MethodInstanceModel? ResolveImplementation(
        CliTypeIdentity receiver,
        MethodInstanceModel declaration)
    {
        if (relationships.Classify(receiver, declaration.DeclaringType)
                .IsSzArrayGenericInterface &&
            ResolveArrayHelperName(declaration) is { } helperName)
        {
            var helperType = types.FindType(
                "System.Collections.Generic.SZArrayHelper");
            var helper = helperType.Methods
                .Select(methods.GetMethod)
                .Single(method => method.Name == helperName &&
                                  method.GenericArity == 1);
            return ResolveInstance(
                helper,
                [],
                [declaration.DeclaringType.TypeArguments[0]]);
        }
        var interfaceDispatch = typeDefinitions.ResolveTypeIdentity(
            declaration.DeclaringType).IsInterface;
        for (var current = receiver;
             current is not null;
             current = baseTypes.Resolve(current))
        {
            if (current.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
            {
                continue;
            }
            var implementations = methodImplementations.GetMethodImplementations(current);
            var explicitImplementation = implementations.FirstOrDefault(mapping =>
                    mapping.Declaration.Definition.Key == declaration.Definition.Key &&
                    mapping.Declaration.DeclaringType.Equals(
                        declaration.DeclaringType) &&
                    SameSignature(
                        WithMethodArguments(mapping.Declaration, declaration.MethodArguments)
                            .Signature,
                        declaration.Signature));
            explicitImplementation ??= implementations.FirstOrDefault(mapping =>
                mapping.Declaration.Definition.Key == declaration.Definition.Key &&
                relationships.Classify(
                    mapping.Declaration.DeclaringType,
                    declaration.DeclaringType).RequiresVariantMethodResolution);
            if (explicitImplementation is not null)
            {
                return WithMethodArguments(
                    explicitImplementation.Body,
                    declaration.MethodArguments);
            }

            var definition = typeDefinitions.ResolveTypeIdentity(current);
            var candidates = definition.Methods
                .Select(methods.GetMethod)
                .Where(method => method.Name == declaration.Definition.Name)
                .Where(method => method.GenericArity == declaration.MethodArguments.Length)
                .Select(method => InstanceFor(
                    method,
                    current,
                    declaration.MethodArguments))
                .ToArray();
            if (interfaceDispatch)
            {
                var matching = candidates
                    .Where(method => SameSignature(method.Signature, declaration.Signature))
                    .ToArray();
                var implicitImplementation = matching.Length > 1
                    ? DeclaredImplementation(matching, current, definition, declaration)
                    : matching.SingleOrDefault();
                if (implicitImplementation is null &&
                    implementedInterfaces.GetInterfaces(current).Any(implemented =>
                        relationships.Classify(
                            implemented,
                            declaration.DeclaringType).RequiresVariantMethodResolution))
                {
                    implicitImplementation = candidates.SingleOrDefault(method =>
                        IsSubstitutableSignature(
                            method.Signature,
                            declaration.Signature));
                }
                if (implicitImplementation is not null)
                {
                    return implicitImplementation;
                }
            }
            else
            {
                var matching = candidates
                    .Where(method => SameSignature(method.Signature, declaration.Signature))
                    .ToArray();
                // In the declaring type, the declaration itself, also where another
                // overload's signature coincides with it once the type's parameters
                // are substituted.
                var overrideMethod = current.Equals(declaration.DeclaringType)
                    ? matching.FirstOrDefault(method => method.Definition.Key == declaration.Definition.Key) ??
                        matching.SingleOrDefault()
                    : matching.SingleOrDefault(method => method.Definition.IsVirtual && !method.Definition.IsNewSlot);
                if (overrideMethod is not null)
                {
                    return overrideMethod;
                }
            }
        }
        if (interfaceDispatch && !declaration.Definition.IsAbstract)
        {
            return declaration;
        }
        return null;
    }

    private string? ResolveArrayHelperName(MethodInstanceModel declaration)
    {
        var interfaceName = typeDefinitions.ResolveTypeIdentity(
            declaration.DeclaringType).FullName;
        return (interfaceName, declaration.Definition.Name) switch
        {
            ("System.Collections.Generic.IEnumerable`1", "GetEnumerator") =>
                "GetEnumerator",
            ("System.Collections.Generic.ICollection`1", "get_Count") or
            ("System.Collections.Generic.IReadOnlyCollection`1", "get_Count") =>
                "GetCount",
            ("System.Collections.Generic.ICollection`1", "get_IsReadOnly") =>
                "GetIsReadOnly",
            ("System.Collections.Generic.ICollection`1", "Add") => "Add",
            ("System.Collections.Generic.ICollection`1", "Clear") => "Clear",
            ("System.Collections.Generic.ICollection`1", "Contains") => "Contains",
            ("System.Collections.Generic.ICollection`1", "CopyTo") => "CopyTo",
            ("System.Collections.Generic.ICollection`1", "Remove") => "Remove",
            ("System.Collections.Generic.IList`1", "get_Item") or
            ("System.Collections.Generic.IReadOnlyList`1", "get_Item") =>
                "GetItem",
            ("System.Collections.Generic.IList`1", "set_Item") => "SetItem",
            ("System.Collections.Generic.IList`1", "IndexOf") => "IndexOf",
            ("System.Collections.Generic.IList`1", "Insert") => "Insert",
            ("System.Collections.Generic.IList`1", "RemoveAt") => "RemoveAt",
            _ => null,
        };
    }

    private MethodInstanceModel InstanceFor(
        MethodDefinitionModel definition,
        CliTypeIdentity declaringType,
        ImmutableArray<CliTypeIdentity> methodArguments) =>
        ResolveInstance(
            definition,
            declaringType.Shape == CliTypeShape.GenericInstantiation
                ? declaringType.TypeArguments
                : [],
            methodArguments);

    private MethodInstanceModel ResolveInstance(
        MethodDefinitionModel method,
        ImmutableArray<CliTypeIdentity> typeArguments,
        ImmutableArray<CliTypeIdentity> methodArguments) =>
        methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format(method),
            0,
            new CliGenericContext(typeArguments, methodArguments));

    private MethodInstanceModel WithMethodArguments(
        MethodInstanceModel method,
        ImmutableArray<CliTypeIdentity> methodArguments) =>
        ResolveInstance(
            method.Definition,
            method.DeclaringType.Shape == CliTypeShape.GenericInstantiation
                ? method.DeclaringType.TypeArguments
                : [],
            methodArguments);

    // Overloads such as Box<T>.N(T) and N(int) coincide once T is int. The
    // implementation is the one whose declared signature matches the interface
    // method's, read through the interface as the type declares it, so N(T)
    // implements IShape<T>.N (ECMA-335 II.12.2).
    private MethodInstanceModel? DeclaredImplementation(
        MethodInstanceModel[] matching,
        CliTypeIdentity current,
        TypeDefinitionModel definition,
        MethodInstanceModel declaration)
    {
        if (current.Shape != CliTypeShape.GenericInstantiation)
        {
            return null;
        }

        var open = CliTypeIdentity.GenericInstantiation(
            current.ElementType!,
            [.. Enumerable.Range(0, definition.GenericArity)
                .Select(index => CliTypeIdentity.GenericParameter(method: false, index))]);
        var declared = implementedInterfaces.GetInterfaces(open)
            .Where(implemented => implemented.Substitute(current.TypeArguments).Equals(declaration.DeclaringType))
            .Select(implemented => declaration.Definition.Signature.Substitute(implemented.TypeArguments))
            .ToArray();
        return matching.SingleOrDefault(method =>
            declared.Any(signature => SameSignature(method.Definition.Signature, signature)));
    }

    private static bool SameSignature(
        MethodSignatureModel left,
        MethodSignatureModel right) =>
        left.ReturnSignatureType.Equals(right.ReturnSignatureType) &&
        left.ParameterSignatureTypes.SequenceEqual(right.ParameterSignatureTypes);

    private bool IsSubstitutableSignature(
        MethodSignatureModel implementation,
        MethodSignatureModel declaration) =>
        implementation.ParameterSignatureTypes.Length ==
            declaration.ParameterSignatureTypes.Length &&
        IsAssignableOrEqual(
            implementation.ReturnSignatureType,
            declaration.ReturnSignatureType) &&
        implementation.ParameterSignatureTypes
            .Zip(declaration.ParameterSignatureTypes)
            .All(pair => IsAssignableOrEqual(pair.Second, pair.First));

    private bool IsAssignableOrEqual(
        CliTypeIdentity candidate,
        CliTypeIdentity target) =>
        candidate.Equals(target) || relationships.Classify(candidate, target).IsAssignmentCompatible;
}
