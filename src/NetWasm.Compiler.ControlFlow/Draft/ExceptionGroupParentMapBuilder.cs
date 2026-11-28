// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

        var containersOf = Containers(exceptionGroups, sources);
        foreach (var candidate in exceptionGroups)
        {
            var containers = containersOf[candidate]
                .Where(container => exceptionScopes.Find(sources[container], sources[candidate]) is not null)
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

    // For each group, the other groups with a scope (try, filter, handler) open where it starts and reaching past its
    // extent: its possible containers, found by one sweep over the groups in try order with the scopes open there, as
    // many as the nesting is deep, rather than every group per group. Find confirms each.
    private static Dictionary<StructuredExceptionGroupDraft, List<StructuredExceptionGroupDraft>> Containers(
        ImmutableArray<StructuredExceptionGroupDraft> groups,
        Dictionary<StructuredExceptionGroupDraft, ExceptionGroupSource> sources)
    {
        var scopes = new List<(int Start, int End, int Owner)>();
        for (var owner = 0; owner < groups.Length; owner++)
        {
            var source = sources[groups[owner]];
            scopes.Add((source.TryOffset, source.TryEnd, owner));
            foreach (var region in source.Regions)
            {
                if (region.FilterOffset is int filter) scopes.Add((filter, region.HandlerOffset, owner));
                scopes.Add((region.HandlerOffset, checked(region.HandlerOffset + region.HandlerLength), owner));
            }
        }
        var byStart = scopes.OrderBy(scope => scope.Start).ToArray();
        var containers = new Dictionary<StructuredExceptionGroupDraft, List<StructuredExceptionGroupDraft>>(
            ReferenceEqualityComparer.Instance);
        var open = new List<(int Start, int End, int Owner)>();
        var next = 0;
        foreach (var candidate in Enumerable.Range(0, groups.Length).OrderBy(index => sources[groups[index]].TryOffset))
        {
            var source = sources[groups[candidate]];
            while (next < byStart.Length && byStart[next].Start <= source.TryOffset) open.Add(byStart[next++]);
            open.RemoveAll(scope => scope.End < source.TryOffset);
            var extent = source.Extent;
            containers[groups[candidate]] =
            [
                .. open.Where(scope => scope.Owner != candidate && scope.End >= extent)
                    .Select(scope => scope.Owner)
                    .Distinct()
                    .Order()
                    .Select(owner => groups[owner]),
            ];
        }
        return containers;
    }
}
