using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionAwareDispatcherBlockSelector : IExceptionAwareDispatcherBlockSelector
{
    public ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(allowed);

        var exceptionGroups = state.ExceptionGroupsByEntry
            .Where(entry => component.Contains(entry.Key))
            .Select(entry => entry.Value)
            .ToArray();
        var excluded = allowed
            .Where(block => exceptionGroups.Any(group =>
            {
                var offset = state.Graph.GetBlock(block).StartOffset;
                var extent = group.Clauses.Max(clause => checked(
                    clause.Region.HandlerOffset + clause.Region.HandlerLength));
                return offset >= group.TryOffset && offset < extent;
            }))
            .ToImmutableHashSet();
        var owned = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Stack<int>(component.Where(block => !excluded.Contains(block)));
        while (pending.TryPop(out var block))
        {
            if (!component.Contains(block) || excluded.Contains(block) || !owned.Add(block))
            {
                continue;
            }

            foreach (var successor in state.Graph.Successors[block])
            {
                pending.Push(successor);
            }
        }

        return owned.ToImmutable();
    }
}
