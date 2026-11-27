// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

internal interface IReceiverFlowAnalyzer
{
    ReceiverFlowResult Analyze(ReachableProgram program, ReachabilityRoots roots);
}

// The report, and for each dispatched call whose receivers the analysis follows precisely over the methods flow
// reaches (the sets step C dispatches on), those receiver types.
internal sealed record ReceiverFlowResult(
    ReceiverFlowReport Report,
    ImmutableDictionary<string, ImmutableArray<CliTypeIdentity>> PreciseReceivers);

// Runs the receiver flow analysis over a finished program with dispatch unchanged (RECEIVER_TYPE_FLOW_DESIGN.md, step
// A), then reaches every method the closure kept, as a boundary, to bound the precision from the other side.
internal sealed class ReceiverFlowAnalyzer(ReceiverFlowServices services) : IReceiverFlowAnalyzer
{
    private readonly ReceiverFlowServices _services = services ?? throw new ArgumentNullException(nameof(services));

    public ReceiverFlowResult Analyze(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var started = Stopwatch.GetTimestamp();
        var engine = new ReceiverFlowEngine(_services, new ReachableProgramFlowView(program, roots));
        engine.ReachRoots();
        engine.Settle();
        var reachedOnly = engine.Precision();
        var precise = engine.PreciseReceivers();
        var reachedOnlyMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var flowReached = engine.ReachedCount;
        var unreached = engine.UnreachedSample(20);
        var listed = engine.ListedSites();
        engine.ReachEverything();
        engine.Settle();
        var everything = engine.Precision();
        return new(new ReceiverFlowReport(engine.MethodCount, flowReached, unreached, engine.Graph.NodeCount,
            engine.Graph.EdgeCount, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, reachedOnlyMilliseconds,
            reachedOnly, everything, listed, engine.Warnings()), precise);
    }
}
