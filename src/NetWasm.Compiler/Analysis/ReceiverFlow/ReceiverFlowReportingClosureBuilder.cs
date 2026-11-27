// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

// Runs the receiver flow analysis once over the final program, after every reduction round, and attaches its report.
// With an audit failure method (the receiver flow audit, step B), each dispatched call whose receivers the analysis
// follows precisely, and whose set is smaller than type-based dispatch's, carries its set and a message: a receiver
// outside the set fails there.
internal sealed class ReceiverFlowReportingClosureBuilder(
    IReachabilityClosureBuilder inner,
    IReceiverFlowAnalyzer analyzer,
    IAnalysisStepRecorder steps,
    EntityKey? auditFailure = null) : IReachabilityClosureBuilder
{
    private readonly IReachabilityClosureBuilder _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IReceiverFlowAnalyzer _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    private readonly IAnalysisStepRecorder _steps = steps ?? throw new ArgumentNullException(nameof(steps));

    public ReachableProgram Build(MethodDefinitionModel? entryPoint, IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null, ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
    {
        var program = _inner.Build(entryPoint, requestedExports, roots, rewrittenBodies);
        var result = _steps.Record("receiver flow", () => _analyzer.Analyze(program, roots ?? ReachabilityRoots.Empty));
        program = program with { ReceiverFlow = result.Report };
        return auditFailure is { } key && program.Methods.TryGetValue(key, out var failure)
            ? Audited(program, result.PreciseReceivers, failure.Method)
            : program;
    }

    private static ReachableProgram Audited(ReachableProgram program,
        ImmutableDictionary<string, ImmutableArray<CliTypeIdentity>> precise, MethodInstanceModel failure)
    {
        var sites = program.DispatchCallSites.ToBuilder();
        var messages = new List<string>();
        foreach (var (key, receivers) in precise)
        {
            if (!program.DispatchCallSites.TryGetValue(key, out var site) || site.SharesObjectDispatch ||
                receivers.Length >= site.Targets.Select(static target => target.ReceiverType).Distinct().Count())
            {
                continue;
            }
            var message = $"NetWasm receiver flow audit: a receiver outside the analysis's set at {key}";
            messages.Add(message);
            sites[key] = site with { AuditedReceivers = receivers, AuditMessage = message, AuditFailure = failure };
        }
        return program with
        {
            DispatchCallSites = sites.ToImmutable(),
            StringLiterals = program.StringLiterals.Union(messages),
        };
    }
}
