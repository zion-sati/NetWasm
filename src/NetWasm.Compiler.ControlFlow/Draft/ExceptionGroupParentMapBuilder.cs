using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class ExceptionGroupParentMapBuilder(IExceptionScopeFinder exceptionScopes) :
    IExceptionGroupParentMapBuilder
{
    public IReadOnlyDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?> Build(
        ImmutableArray<StructuredExceptionGroupDraft> exceptionGroups)
    {
        var parents = new Dictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?>(
            ReferenceEqualityComparer.Instance);
        var sources = new Dictionary<StructuredExceptionGroupDraft, ExceptionGroupSource>(
            ReferenceEqualityComparer.Instance);
        foreach (var exceptionGroup in exceptionGroups)
        {
            sources.Add(
                exceptionGroup,
                new ExceptionGroupSource(
                    exceptionGroup.TryOffset,
                    exceptionGroup.TryLength,
                    [.. exceptionGroup.Clauses.Select(clause => clause.Region)]));
        }

        foreach (var candidate in exceptionGroups)
        {
            var containers = exceptionGroups
                .Where(container =>
                    !ReferenceEquals(container, candidate) &&
                    exceptionScopes.Find(sources[container], sources[candidate]) is not null)
                .ToImmutableArray();
            var immediate = containers
                .Where(container =>
                    !containers.Any(other =>
                        !ReferenceEquals(other, container) &&
                        exceptionScopes.Find(sources[container], sources[other]) is not null))
                .ToImmutableArray();

            if (immediate.Length > 1)
            {
                throw new InvalidOperationException(
                    "An exception group has more than one immediate lexical parent.");
            }

            parents.Add(candidate, immediate.Length == 0 ? null : immediate[0]);
        }

        return parents;
    }
}
