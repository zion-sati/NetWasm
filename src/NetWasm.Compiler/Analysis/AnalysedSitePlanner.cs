using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>The dispatch and type-test sites of methods analysed alone.</summary>
internal sealed record AnalysedSites(
    ImmutableDictionary<string, DispatchCallSiteModel> DispatchCallSites,
    ImmutableDictionary<string, TypeTestSiteModel> TypeTestSites);

internal interface IAnalysedSitePlanner
{
    // The sites of the analysed methods, resolved against the types a whole program
    // allocates as that program's closure resolves them.
    AnalysedSites Plan(IReadOnlyCollection<ReachableMethodAnalysis> analyses, IReadOnlyCollection<CliTypeIdentity> allocatedTypes);
}

internal sealed class AnalysedSitePlanner(
    IDispatchCandidateIndexFactory candidates,
    IDispatchTargetResolver targets,
    ITypeTestPlanner typeTests) : IAnalysedSitePlanner
{
    public AnalysedSites Plan(IReadOnlyCollection<ReachableMethodAnalysis> analyses, IReadOnlyCollection<CliTypeIdentity> allocatedTypes)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(allocatedTypes);
        var index = candidates.Create();
        foreach (var type in allocatedTypes) index.Index(new DispatchReceiverCandidate(type));
        var dispatch = ImmutableDictionary.CreateBuilder<string, DispatchCallSiteModel>(StringComparer.Ordinal);
        foreach (var analysis in analyses)
        {
            foreach (var site in analysis.Instructions.Dispatches)
            {
                // As the closure keeps a site's targets: one per receiver, in canonical order.
                var found = new Dictionary<string, DispatchTargetModel>(StringComparer.Ordinal);
                foreach (var candidate in index.Index(new DispatchDeclarationCandidate(site.Key, site.Declaration)))
                {
                    if (targets.Resolve(candidate.Declaration.Declaration, candidate.Receiver) is { } target)
                        found.TryAdd(target.ReceiverType.CanonicalName, target);
                }
                dispatch[site.Key] = new(site.Declaration.Caller, site.Declaration.IlOffset, site.Declaration.Declaration,
                [
                    .. found.Values
                        .OrderBy(target => target.ReceiverType.CanonicalName, StringComparer.Ordinal)
                        .ThenBy(target => target.Method.CanonicalName, StringComparer.Ordinal),
                ]);
            }
        }
        return new(dispatch.ToImmutable(), typeTests.Build(analyses.Select(analysis => analysis.Body), allocatedTypes));
    }
}
