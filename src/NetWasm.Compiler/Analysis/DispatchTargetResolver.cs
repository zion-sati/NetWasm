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
        return typeDefinitions.ResolveTypeIdentity(declaration.DeclaringType).IsInterface
            ? ResolveInterfaceImplementation(receiver, declaration)
            : ResolveOverride(receiver, declaration);
    }

    // ECMA-335 II.12.2: the most derived type with a MethodImpl for the interface
    // method, or that declares the interface and so maps the method to its own or
    // a base type's public virtual method of that name and signature, provides
    // the slot; a type that does not declare the interface keeps its base type's
    // mapping. The receiver runs that slot's override. An exact implementation
    // of the interface comes before a variant one, and with no class
    // implementation the most specific default implementation runs.
    private MethodInstanceModel? ResolveInterfaceImplementation(
        CliTypeIdentity receiver,
        MethodInstanceModel declaration)
    {
        foreach (var variant in (ReadOnlySpan<bool>)[false, true])
        {
            for (var current = receiver;
                 current is not null;
                 current = baseTypes.Resolve(current))
            {
                if (current.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
                {
                    continue;
                }
                if (ExplicitImplementation(current, declaration, variant) is { } body)
                {
                    return ResolveOverride(receiver, body);
                }
                if (DeclaredInterface(current, declaration.DeclaringType, variant) is not { } declared)
                {
                    continue;
                }
                var expected = ResolveInstance(
                    declaration.Definition,
                    declared.Shape == CliTypeShape.GenericInstantiation ? declared.TypeArguments : [],
                    declaration.MethodArguments);
                for (var holder = current; holder is not null; holder = baseTypes.Resolve(holder))
                {
                    if (ImplicitImplementation(holder, declaration, expected) is { } slot)
                    {
                        return ResolveOverride(receiver, slot);
                    }
                }
            }
        }
        return DefaultImplementation(receiver, declaration);
    }

    private MethodInstanceModel? ExplicitImplementation(
        CliTypeIdentity type,
        MethodInstanceModel declaration,
        bool variant) =>
        methodImplementations.GetMethodImplementations(type)
            .Where(mapping => Implements(mapping.Declaration, declaration, variant))
            .Select(mapping => WithMethodArguments(mapping.Body, declaration.MethodArguments))
            .FirstOrDefault();

    private bool Implements(MethodInstanceModel implemented, MethodInstanceModel declaration, bool variant) =>
        implemented.Definition.Key == declaration.Definition.Key &&
        (variant
            ? relationships.Classify(implemented.DeclaringType, declaration.DeclaringType)
                .RequiresVariantMethodResolution
            : implemented.DeclaringType.Equals(declaration.DeclaringType) &&
              SameSignature(
                  WithMethodArguments(implemented, declaration.MethodArguments).Signature,
                  declaration.Signature));

    private CliTypeIdentity? DeclaredInterface(CliTypeIdentity type, CliTypeIdentity interfaceType, bool variant) =>
        implementedInterfaces.GetInterfaces(type).FirstOrDefault(declared => variant
            ? relationships.Classify(declared, interfaceType).RequiresVariantMethodResolution
            : declared.Equals(interfaceType));

    // The type's public virtual method with the interface method's name and its
    // signature as the declared interface gives it.
    private MethodInstanceModel? ImplicitImplementation(
        CliTypeIdentity holder,
        MethodInstanceModel declaration,
        MethodInstanceModel expected)
    {
        var definition = typeDefinitions.ResolveTypeIdentity(holder);
        var matching = VirtualMethods(holder, definition, declaration.Definition.Name, declaration.MethodArguments)
            .Where(method => method.Definition.IsPublic && SameSignature(method.Signature, expected.Signature))
            .ToArray();
        return matching.Length > 1
            ? DeclaredImplementation(matching, holder, definition, expected)
            : matching.SingleOrDefault();
    }

    // ECMA-335 II.10.3: from the slot's type down to the receiver, a virtual
    // method of the slot's name and signature replaces its body until a new slot
    // of that name and signature hides it. A MethodImpl replaces the body when it
    // names a method whose own slot this is, or, from a method that preserves
    // base overrides (a covariant override), any method that replaced it.
    private MethodInstanceModel ResolveOverride(CliTypeIdentity receiver, MethodInstanceModel slot)
    {
        var chain = new List<CliTypeIdentity>();
        var current = receiver;
        while (current is not null && !current.Equals(slot.DeclaringType))
        {
            chain.Add(current);
            current = baseTypes.Resolve(current);
        }
        if (current is null)
        {
            return slot;
        }
        var body = slot;
        var ownSlot = new HashSet<EntityKey> { slot.Definition.Key };
        var replacing = new HashSet<EntityKey> { slot.Definition.Key };
        var visible = true;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var type = chain[index];
            if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
            {
                continue;
            }
            var replaced = methodImplementations.GetMethodImplementations(type).FirstOrDefault(mapping =>
                ownSlot.Contains(mapping.Declaration.Definition.Key) ||
                mapping.Body.Definition.PreservesBaseOverrides &&
                replacing.Contains(mapping.Declaration.Definition.Key));
            if (replaced is not null)
            {
                body = WithMethodArguments(replaced.Body, slot.MethodArguments);
                replacing.Add(body.Definition.Key);
                if (!body.Definition.IsNewSlot)
                {
                    ownSlot.Add(body.Definition.Key);
                }
                continue;
            }
            if (!visible)
            {
                continue;
            }
            var definition = typeDefinitions.ResolveTypeIdentity(type);
            var matching = VirtualMethods(type, definition, slot.Definition.Name, slot.MethodArguments)
                .Where(method => SameSignature(method.Signature, slot.Signature))
                .ToArray();
            if ((matching.Length > 1 ? DeclaredOverride(matching, type, definition, slot) : matching.SingleOrDefault())
                is not { } same)
            {
                continue;
            }
            if (same.Definition.IsNewSlot)
            {
                visible = false;
                continue;
            }
            body = same;
            ownSlot.Add(same.Definition.Key);
            replacing.Add(same.Definition.Key);
        }
        return body;
    }

    // Overloads such as G<T>.M(T) and M(int) coincide once T is int. The method
    // that overrides the slot is the one whose declared signature is the slot's
    // as the type declares its base (ECMA-335 II.10.3.2). Only an instantiated
    // generic type's methods coincide, so the type is one.
    private MethodInstanceModel? DeclaredOverride(
        MethodInstanceModel[] matching,
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        MethodInstanceModel slot)
    {
        var open = CliTypeIdentity.GenericInstantiation(
            type.ElementType!,
            [.. Enumerable.Range(0, definition.GenericArity)
                .Select(index => CliTypeIdentity.GenericParameter(method: false, index))]);
        var slotType = typeDefinitions.ResolveTypeIdentity(slot.DeclaringType).Key;
        var baseType = baseTypes.Resolve(open);
        while (baseType is not null && typeDefinitions.ResolveTypeIdentity(baseType).Key != slotType)
        {
            baseType = baseTypes.Resolve(baseType);
        }
        if (baseType is null)
        {
            return null;
        }
        var declared = slot.Definition.Signature.Substitute(
            baseType.Shape == CliTypeShape.GenericInstantiation ? baseType.TypeArguments : []);
        return matching.SingleOrDefault(method => SameSignature(method.Definition.Signature, declared));
    }

    private IEnumerable<MethodInstanceModel> VirtualMethods(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        string name,
        ImmutableArray<CliTypeIdentity> methodArguments) =>
        definition.Methods
            .Select(methods.GetMethod)
            .Where(method => method.Name == name &&
                             method.IsVirtual &&
                             !method.IsStatic &&
                             method.GenericArity == methodArguments.Length)
            .Select(method => InstanceFor(method, type, methodArguments));

    // With no class implementation, the default implementations the receiver's
    // interfaces provide: the interface method's own body and each interface's
    // MethodImpl for it. The most specific is the one whose interface derives
    // from every other's; with none, there is no implementation.
    private MethodInstanceModel? DefaultImplementation(
        CliTypeIdentity receiver,
        MethodInstanceModel declaration)
    {
        var interfaces = AllInterfaces(receiver);
        var candidates = new List<(CliTypeIdentity Owner, MethodInstanceModel Body)>();
        if (!declaration.Definition.IsAbstract)
        {
            candidates.Add((declaration.DeclaringType, declaration));
        }
        foreach (var owner in interfaces)
        {
            foreach (var variant in (ReadOnlySpan<bool>)[false, true])
            {
                if (ExplicitImplementation(owner, declaration, variant) is { } body)
                {
                    candidates.Add((owner, body));
                    break;
                }
            }
        }
        var specific = candidates
            .Where(candidate => candidates.All(other =>
                other.Owner.Equals(candidate.Owner) || AllInterfaces(candidate.Owner).Contains(other.Owner)))
            .ToArray();
        return specific.Length == 1 ? specific[0].Body : null;
    }

    private HashSet<CliTypeIdentity> AllInterfaces(CliTypeIdentity type)
    {
        var all = new HashSet<CliTypeIdentity>();
        var pending = new Stack<CliTypeIdentity>();
        for (var current = type; current is not null; current = baseTypes.Resolve(current))
        {
            if (current.Shape is not (CliTypeShape.SzArray or CliTypeShape.Array))
            {
                pending.Push(current);
            }
        }
        while (pending.TryPop(out var next))
        {
            foreach (var implemented in implementedInterfaces.GetInterfaces(next))
            {
                if (all.Add(implemented))
                {
                    pending.Push(implemented);
                }
            }
        }
        return all;
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
    // implements IShape<T>.N (ECMA-335 II.12.2). Only an instantiated generic
    // type's methods coincide, so the type is one.
    private MethodInstanceModel? DeclaredImplementation(
        MethodInstanceModel[] matching,
        CliTypeIdentity current,
        TypeDefinitionModel definition,
        MethodInstanceModel declaration)
    {
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
}
