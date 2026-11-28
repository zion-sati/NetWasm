// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>The dispatch and type-test sites of methods analyzed alone.</summary>
internal sealed record AnalyzedSites(
    ImmutableDictionary<string, DispatchCallSiteModel> DispatchCallSites,
    ImmutableDictionary<string, TypeTestSiteModel> TypeTestSites);

internal interface IAnalyzedSitePlanner
{
    // The sites of the analyzed methods, resolved against the types a whole program
    // allocates as that program's closure resolves them.
    AnalyzedSites Plan(IReadOnlyCollection<ReachableMethodAnalysis> analyses, IReadOnlyCollection<CliTypeIdentity> allocatedTypes);
}

internal sealed class AnalyzedSitePlanner(
    IDispatchCandidateIndexFactory candidates,
    IDispatchTargetResolver targets,
    IDispatchSiteTargetSelector siteTargets,
    ITypeTestPlanner typeTests) : IAnalyzedSitePlanner
{
    public AnalyzedSites Plan(IReadOnlyCollection<ReachableMethodAnalysis> analyses, IReadOnlyCollection<CliTypeIdentity> allocatedTypes)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(allocatedTypes);
        var index = candidates.Create();
        foreach (var type in allocatedTypes) index.Index(new DispatchReceiverCandidate(type));
        // As the closure keeps them: each slot use's targets, one per receiver, then each site's from its slot use's.
        var slots = new Dictionary<DispatchSlot, Dictionary<string, DispatchTargetModel>>();
        var sites = new List<(string Key, DispatchDeclaration Declaration)>();
        foreach (var analysis in analyses)
        {
            foreach (var site in analysis.Instructions.Dispatches)
            {
                sites.Add((site.Key, site.Declaration));
                foreach (var candidate in index.Index(new DispatchDeclarationCandidate(site.Key, site.Declaration)))
                {
                    if (targets.Resolve(candidate.Declaration.Declaration, candidate.Receiver) is not { } target)
                    {
                        continue;
                    }
                    if (!slots.TryGetValue(candidate.Slot, out var found))
                    {
                        slots.Add(candidate.Slot, found = new(StringComparer.Ordinal));
                    }
                    found.TryAdd(target.ReceiverType.CanonicalName, target);
                }
            }
        }
        var ordered = slots.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Values
                .OrderBy(target => target.ReceiverType.CanonicalName, StringComparer.Ordinal)
                .ThenBy(target => target.Method.CanonicalName, StringComparer.Ordinal)
                .ToImmutableArray());
        var dispatch = ImmutableDictionary.CreateBuilder<string, DispatchCallSiteModel>(StringComparer.Ordinal);
        foreach (var (key, declaration) in sites)
        {
            var slot = new DispatchSlot(declaration.Operation, declaration.Declaration.CanonicalName);
            dispatch[key] = new(declaration.Caller, declaration.IlOffset, declaration.Declaration,
                siteTargets.Select(declaration.ReceiverBound, ordered.GetValueOrDefault(slot, [])));
        }
        return new(dispatch.ToImmutable(), typeTests.Build(analyses.Select(analysis => analysis.Body), allocatedTypes));
    }
}
