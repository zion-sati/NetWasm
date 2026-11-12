using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Strategy: compute descriptor roots required by the requested runtime type facts: base types,
// generic arguments a query can return, the declaring type of every nested type, whose open
// definition is what DeclaringType returns for a nested type of a constructed generic, the open
// definition of every constructed generic type, which GetGenericTypeDefinition returns, and the
// element of every array, pointer and by-reference type, which GetElementType returns.
internal sealed class RuntimeTypeFactsClosurePlanner(
    IBaseTypeResolver bases,
    ITypeDefinitionResolver definitions,
    ITypeIdentityResolver identities) : IRuntimeTypeFactsClosurePlanner
{
    private readonly IBaseTypeResolver _bases = bases ?? throw new ArgumentNullException(nameof(bases));
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ITypeIdentityResolver _identities = identities ?? throw new ArgumentNullException(nameof(identities));

    public ImmutableArray<CliTypeIdentity> Plan(
        ImmutableArray<CliTypeIdentity> seeds,
        bool includeGenericArguments,
        bool includeDeclaringTypes,
        bool includeGenericDefinitions,
        bool includeElementTypes)
    {
        if (seeds.IsDefault) throw new ArgumentException("Type-fact seeds must be initialized.", nameof(seeds));
        var pending = new Queue<CliTypeIdentity>();
        var visited = new HashSet<CliTypeIdentity>();
        var required = new HashSet<CliTypeIdentity>();
        foreach (var type in seeds) Enqueue(type);
        while (pending.TryDequeue(out var type))
        {
            if (includeDeclaringTypes && Declaring(type) is { } declaring)
            {
                required.Add(declaring);
                Enqueue(declaring);
            }
            if (includeGenericDefinitions && type.Shape == CliTypeShape.GenericInstantiation)
            {
                required.Add(type.ElementType!);
                Enqueue(type.ElementType!);
            }
            if (includeElementTypes && HasElement(type))
            {
                required.Add(type.ElementType!);
            }
            if (!CanExpand(type)) continue;
            var baseType = _bases.Resolve(type);
            if (baseType is null || !CanExpand(baseType)) continue;
            required.Add(baseType);
            Enqueue(baseType);
        }
        return [.. required];

        void Enqueue(CliTypeIdentity type)
        {
            if (!visited.Add(type)) return;
            pending.Enqueue(type);
            // A metadata query can observe generic arguments even when no method
            // allocates or otherwise references their runtime types.
            if (includeGenericArguments && !type.ContainsGenericParameters) required.Add(type);
            if (type.Shape != CliTypeShape.GenericInstantiation && type.ElementType is not null)
                Enqueue(type.ElementType);
            foreach (var argument in type.TypeArguments) Enqueue(argument);
        }

        bool CanExpand(CliTypeIdentity type) => !type.ContainsGenericParameters &&
            (type.Shape != CliTypeShape.Named || _definitions.ResolveTypeIdentity(type).GenericArity == 0);
    }

    private static bool HasElement(CliTypeIdentity type) => type.Shape is CliTypeShape.SzArray or CliTypeShape.Array or
        CliTypeShape.UnmanagedPointer or CliTypeShape.ManagedByReference;

    // A nested type's enclosing definition: for a nested type of a generic, its open definition.
    private CliTypeIdentity? Declaring(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        _definitions.ResolveTypeIdentity(type).DeclaringType is { } declaring
            ? _identities.GetTypeIdentity(declaring)
            : null;
}
