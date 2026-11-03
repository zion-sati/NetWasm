using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Strategy: compute descriptor roots required by the requested runtime type facts.
internal sealed class RuntimeTypeFactsClosurePlanner(
    IBaseTypeResolver bases,
    ITypeDefinitionResolver definitions) : IRuntimeTypeFactsClosurePlanner
{
    private readonly IBaseTypeResolver _bases = bases ?? throw new ArgumentNullException(nameof(bases));
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public ImmutableArray<CliTypeIdentity> Plan(ImmutableArray<CliTypeIdentity> seeds, bool includeGenericArguments)
    {
        if (seeds.IsDefault) throw new ArgumentException("Type-fact seeds must be initialized.", nameof(seeds));
        var pending = new Queue<CliTypeIdentity>();
        var visited = new HashSet<CliTypeIdentity>();
        var required = new HashSet<CliTypeIdentity>();
        foreach (var type in seeds) Enqueue(type);
        while (pending.TryDequeue(out var type))
        {
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
}
