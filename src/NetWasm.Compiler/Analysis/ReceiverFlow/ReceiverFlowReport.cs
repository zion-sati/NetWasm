// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// What the receiver flow analysis found (RECEIVER_TYPE_FLOW_DESIGN.md, section 8), as a diagnostic artifact: for the
// program's calls whose targets depend on their receivers, the receivers and targets type-based dispatch resolves
// today against those the flow reaches, and why a receiver the analysis could not follow is top.
public sealed record ReceiverFlowReport(
    int Methods,
    int FlowReachedMethods,
    ImmutableArray<string> UnreachedSample,
    int Nodes,
    int Edges,
    long Milliseconds,
    // The time to the first fixpoint, over the methods flow reaches.
    long ReachedOnlyMilliseconds,
    // Every method of the program reached as a boundary too: the precision when nothing the closure kept is ignored.
    ReceiverFlowPrecision ReachedOnly,
    ReceiverFlowPrecision Everything,
    ImmutableArray<ReceiverFlowSite> GenericVirtualSites,
    ImmutableArray<string> ReinterpretationWarnings);

public sealed record ReceiverFlowPrecision(
    ReceiverFlowSiteTotals GenericVirtual,
    ReceiverFlowSiteTotals Virtual,
    ReceiverFlowSiteTotals DelegateInvoke,
    // The distinct methods generic virtual calls dispatch to: each is an instantiation the closure compiles.
    int GenericVirtualTargetsToday,
    int GenericVirtualTargetsByFlow,
    ImmutableArray<ReceiverFlowCause> TopCauses);

public sealed record ReceiverFlowSiteTotals(
    int Sites,
    int PreciseSites,
    long ReceiversToday,
    long ReceiversByFlow,
    long TargetsToday,
    long TargetsByFlow);

public sealed record ReceiverFlowCause(string Cause, int Sites);

public sealed record ReceiverFlowSite(
    string Site,
    string Declaration,
    int TargetsToday,
    int TargetsByFlow,
    bool Precise,
    string? Cause,
    // The receiver types the flow reached the site with (the first few).
    ImmutableArray<string> Receivers);
