using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateFieldAddressResolver(
    IManagedAggregateFieldResolver fields) : IManagedAggregateFieldAddressResolver
{
    private readonly IManagedAggregateFieldResolver _fields = fields ??
        throw new ArgumentNullException(nameof(fields));

    public ManagedAggregateAddressOrigins Resolve(
        CilInstruction instruction,
        ManagedAggregateAddressOrigins origins,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(session);
        if (origins.Unknown)
        {
            session.Reject(origins);
            return ManagedAggregateAddressOrigins.UnknownOnly;
        }

        var recipes = ImmutableHashSet.CreateBuilder<ManagedAggregateAddressRecipe>();
        foreach (var recipe in origins.Recipes)
        {
            if (!_fields.TryResolve(recipe.Type, instruction.Operand,
                    out var field, out var fieldType))
            {
                session.Reject(recipe.Storage);
                continue;
            }
            if (recipe.ByteOffset + field.Offset < 0 ||
                recipe.ByteOffset + field.Offset + field.Size > session.Storages[recipe.Storage].Size)
            {
                session.Reject(recipe.Storage);
                continue;
            }
            recipes.Add(recipe with
            {
                ByteOffset = recipe.ByteOffset + field.Offset,
                Type = fieldType,
            });
        }
        return new(false, recipes.ToImmutable());
    }
}
