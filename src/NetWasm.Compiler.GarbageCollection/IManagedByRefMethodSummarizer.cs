using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

/// <summary>One method's by-ref effects, analysed alone against every other method's.</summary>
public interface IManagedByRefMethodSummarizer
{
    ManagedByRefMethodSummary Summarize(ManagedByRefMethodSummaryRequest request);
}

/// <summary>
/// A method to summarize: its identity and body, every method of the program by identity
/// (and each ordinary one by definition), the effects of those that have any, and the
/// dispatch sites its virtual calls resolve through.
/// </summary>
public sealed record ManagedByRefMethodSummaryRequest(
    string Identity,
    ManagedMethodBody Method,
    IReadOnlySet<string> Methods,
    IReadOnlyDictionary<EntityKey, string> DirectMethods,
    IReadOnlyDictionary<string, ManagedByRefMethodEffects> Effects,
    IReadOnlyDictionary<string, DispatchCallSiteModel> DispatchCallSites);

/// <summary>A method's effects, and the methods whose effects they were read from.</summary>
public sealed record ManagedByRefMethodSummary(ManagedByRefMethodEffects Effects, ImmutableArray<string> Callees);

public sealed class ManagedByRefMethodSummarizer(
    IMethodRepository methods,
    IRuntimeIntrinsicRegistry intrinsics,
    IIndexedWorkExecutor work) : IManagedByRefMethodSummarizer
{
    private readonly ManagedByRefEffectAnalyzer _analyzer = new(methods, intrinsics, work, 1);

    public ManagedByRefMethodSummary Summarize(ManagedByRefMethodSummaryRequest request) =>
        _analyzer.AnalyzeAlone(request);
}
