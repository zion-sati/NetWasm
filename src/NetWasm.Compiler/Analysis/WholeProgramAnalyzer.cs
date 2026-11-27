// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Analysis.Reachability;

namespace NetWasm.Compiler.Analysis;

// Builder for the immutable reachable-program closure. Composition happens in
// WholeProgramAnalyzerFactory; this type owns only one Build action.
internal sealed class ReachabilityClosureBuilder(
    IReachabilitySessionFactory sessions,
    IReachabilityRootSeeder roots,
    IReachabilityQueueDrainer queues,
    IReachabilityDerivedRootExpander derivedRoots,
    IReachableProgramBuilder programBuilder,
    IDelegateTypeRecognizer delegateTypes,
    ITypeTestPlanner typeTestPlanner,
    IModuleInitializerOrderer moduleInitializerOrderer,
    IAnalysisStepRecorder steps,
    ICompilerMemoryReleaser memory,
    ReceiverFlow.IClosureReceiverFlowFactory receiverFlows) : IReachabilityClosureBuilder
{
    private readonly IReachabilitySessionFactory _sessions = sessions ??
        throw new ArgumentNullException(nameof(sessions));
    private readonly IReachabilityRootSeeder _roots = roots ??
        throw new ArgumentNullException(nameof(roots));
    private readonly IReachabilityQueueDrainer _queues = queues ??
        throw new ArgumentNullException(nameof(queues));
    private readonly IReachabilityDerivedRootExpander _derivedRoots = derivedRoots ??
        throw new ArgumentNullException(nameof(derivedRoots));
    private readonly IReachableProgramBuilder _programBuilder = programBuilder ??
        throw new ArgumentNullException(nameof(programBuilder));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));
    private readonly ITypeTestPlanner _typeTestPlanner = typeTestPlanner ??
        throw new ArgumentNullException(nameof(typeTestPlanner));
    private readonly IModuleInitializerOrderer _moduleInitializerOrderer = moduleInitializerOrderer ??
        throw new ArgumentNullException(nameof(moduleInitializerOrderer));
    private readonly IAnalysisStepRecorder _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    private readonly ICompilerMemoryReleaser _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ReceiverFlow.IClosureReceiverFlowFactory _receiverFlows =
        receiverFlows ?? throw new ArgumentNullException(nameof(receiverFlows));

    // The closure: the roots are queued, then the queues drain and the derived roots are added until neither adds
    // anything.
    public ReachableProgram Build(
        MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null,
        ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
    {
        ArgumentNullException.ThrowIfNull(requestedExports);
        roots ??= ReachabilityRoots.Empty;
        var exports = requestedExports.ToArray();
        var outwardBoundaries = exports
            .Select(export => export.Method)
            .ToHashSet();
        if (entryPoint is not null)
        {
            outwardBoundaries.Add(entryPoint.Key);
        }
        using var session = _sessions.Create(entryPoint, outwardBoundaries, rewrittenBodies);
        // Dispatched calls go by receiver flow (RECEIVER_TYPE_FLOW_DESIGN.md, steps C and D): their candidates
        // wait in the gate, and each quiescence settles the flow and releases what it admits.
        var flow = _receiverFlows.Create(session.State, entryPoint, exports, roots);
        session.DispatchGate = flow.Gate;
        _roots.Seed(session, entryPoint, roots, exports);
        for (var iteration = 1; ; iteration++)
        {
            var before = session.CaptureClosureStamp();
            _steps.Record($"drain {iteration}", () => _queues.Drain(session));
            // The flow settles as soon as a drain leaves nothing pending; what it releases is drained before derived
            // roots run, which cost the same whether or not anything changed.
            if (session.State.Pending.Count == 0 && session.State.PendingDispatches.Count == 0 &&
                _steps.Record($"receiver flow {iteration}", flow.Settle) > 0)
            {
                continue;
            }
            _steps.Record($"derived roots {iteration}", () => _derivedRoots.Expand(session));
            if (session.State.Pending.Count == 0 && session.State.PendingDispatches.Count == 0)
                _steps.Record($"receiver flow {iteration} after roots", flow.Settle);
            // A drain and an expansion allocate far more than they keep (84 GB for an 11 GB ledger on Devolutions'
            // Avalonia sample), and Satori keeps what they freed committed.
            _memory.Release();
            if (session.State.Pending.Count == 0 &&
                session.State.PendingDispatches.Count == 0 &&
                session.CaptureClosureStamp() == before)
            {
                break;
            }
        }
        var snapshot = _steps.Record("ledger snapshot", () => ReachabilityLedgerSnapshot.From(session.State) with
        {
            ReceiverFlowAdmissions = flow.SiteAdmissions(),
        });
        var program = _steps.Record("program", () => _programBuilder.Build(
            entryPoint,
            [.. exports],
            snapshot,
            _delegateTypes,
            _typeTestPlanner));
        return program with
        {
            ModuleInitializers = _moduleInitializerOrderer.Order(program.ModuleInitializers),
        };
    }
}

// Facade for the reachability closure builder used by the compiler pipeline.
internal sealed class WholeProgramAnalyzer(
    IReachabilityClosureBuilder closureBuilder) : IWholeProgramAnalyzer
{
    private readonly IReachabilityClosureBuilder _closureBuilder =
        closureBuilder ?? throw new ArgumentNullException(nameof(closureBuilder));

    public ReachableProgram Analyze(
        MethodDefinitionModel? entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null) =>
        _closureBuilder.Build(entryPoint, requestedExports, roots);
}
