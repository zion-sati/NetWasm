using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
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
    IModuleInitializerOrderer moduleInitializerOrderer) : IReachabilityClosureBuilder
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
        _roots.Seed(session, entryPoint, roots, exports);
        while (true)
        {
            var before = session.CaptureClosureStamp();
            _queues.Drain(session);
            _derivedRoots.Expand(session);
            if (session.State.Pending.Count == 0 &&
                session.State.PendingDispatches.Count == 0 &&
                session.CaptureClosureStamp() == before)
            {
                break;
            }
        }
        var program = _programBuilder.Build(
            entryPoint,
            [.. exports],
            ReachabilityLedgerSnapshot.From(session.State),
            _delegateTypes,
            _typeTestPlanner);
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
