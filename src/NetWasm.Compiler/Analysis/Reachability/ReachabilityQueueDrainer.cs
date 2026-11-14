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

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class ReachabilityQueueDrainer(
    IReachableMethodApplier methodApplier,
    IReachabilityImportApplier importApplier,
    IDispatchTargetResolver dispatchTargets,
    IManagedCallSiteLedgerWriter managedCallSites,
    ICompilerDiagnosticProvenanceBinder provenance,
    IReachabilityClosureObserver closureObserver) : IReachabilityQueueDrainer
{
    private readonly IReachableMethodApplier _methodApplier = methodApplier ??
        throw new ArgumentNullException(nameof(methodApplier));
    private readonly IReachabilityImportApplier _importApplier = importApplier ??
        throw new ArgumentNullException(nameof(importApplier));
    private readonly IDispatchTargetResolver _dispatchTargets = dispatchTargets ??
        throw new ArgumentNullException(nameof(dispatchTargets));
    private readonly IManagedCallSiteLedgerWriter _managedCallSites = managedCallSites ??
        throw new ArgumentNullException(nameof(managedCallSites));
    private readonly ICompilerDiagnosticProvenanceBinder _provenance = provenance ??
        throw new ArgumentNullException(nameof(provenance));
    private readonly IReachabilityClosureObserver _closureObserver = closureObserver ??
        throw new ArgumentNullException(nameof(closureObserver));

    public void Drain(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        while (session.State.Pending.Count != 0 || session.State.PendingDispatches.Count != 0 || session.IntrinsicAddressBodies.Count != 0)
        {
            var iterationStarted = Stopwatch.GetTimestamp();
            var methodPublicationTicks = 0L;
            var dispatchCount = 0;
            var resolvedDispatchCount = 0;
            var methodRequests =
                ImmutableArray.CreateBuilder<ReachableMethodRequest>(session.State.Pending.Count);
            while (session.State.Pending.TryDequeue(out var methodInstance))
            {
                var method = methodInstance.Definition;
                session.State.MethodInstances.Add(methodInstance.CanonicalName, methodInstance);
                session.State.Types.Add(method.DeclaringType);
                // Every method is discovered as it is queued; a failure found then surfaces
                // here, with the method's dependency path.
                _ = session.DiscoveredRequests.Remove(methodInstance.CanonicalName, out var discoveredMethod);
                var import = discoveredMethod!.Import!;
                RunOnPath(session, methodInstance, (Discovered: discoveredMethod, Import: import, Applier: _importApplier),
                    static (session, state) =>
                    {
                        state.Discovered.Failure?.Throw();
                        state.Applier.Apply(session, state.Import);
                    });
                if (import.IsHandled)
                {
                    continue;
                }
                methodRequests.Add(discoveredMethod.Request!);
            }
            while (session.IntrinsicAddressBodies.TryDequeue(out var addressBody))
            {
                methodRequests.Add(addressBody);
            }
            foreach (var analysis in session.MethodAnalysis.Batches.Analyze(
                         methodRequests.ToImmutable()))
            {
                var publicationStarted = Stopwatch.GetTimestamp();
                RunOnPath(session, analysis.Method, (Analysis: analysis, Applier: _methodApplier),
                    static (session, state) => state.Applier.Apply(session, state.Analysis));
                _managedCallSites.Write(session.State, analysis.Instructions.CallSites);
                methodPublicationTicks +=
                    Stopwatch.GetTimestamp() - publicationStarted;
            }

            var dispatchStarted = Stopwatch.GetTimestamp();
            while (session.State.PendingDispatches.TryDequeue(out var work))
            {
                dispatchCount++;
                // A non-virtual call dispatches on its receiver only to the method it names.
                var target = work.Declaration.Operation == CilOperation.Call
                    ? new DispatchTargetModel(work.Receiver, work.Declaration.Declaration)
                    : _dispatchTargets.Resolve(
                        work.Declaration.Declaration,
                        work.Receiver);
                if (target is null)
                {
                    continue;
                }

                resolvedDispatchCount++;

                if (!session.State.DispatchTargets.TryGetValue(work.DispatchKey, out var targets))
                {
                    targets = new Dictionary<string, DispatchTargetModel>(
                        StringComparer.Ordinal);
                    session.State.DispatchTargets.Add(work.DispatchKey, targets);
                }
                targets.TryAdd(target.ReceiverType.CanonicalName, target);
                // A delegate's Invoke has no body: its type's invoke helper serves it.
                if (session.IsDelegateInvoke(target.Method))
                {
                    session.State.Types.Add(target.Method.Definition.DeclaringType);
                }
                else
                {
                    var previousPath = session.CurrentDependencyPath;
                    session.CurrentDependencyPath = session.State.DispatchDependencyPaths.GetValueOrDefault(
                        work.DispatchKey, ImmutableStack<CompilerDependencyStep>.Empty);
                    session.CurrentReceiver = work.Receiver;
                    try
                    {
                        session.VisitMethod(CilOperation.CallVirtual, target.Method);
                    }
                    finally
                    {
                        session.CurrentDependencyPath = previousPath;
                        session.CurrentReceiver = null;
                    }
                }
                if (work.Declaration.Operation == CilOperation.LoadVirtualFunction)
                {
                    session.AddCallable(target.Method);
                }
            }
            var dispatchPublicationTicks = Stopwatch.GetTimestamp() - dispatchStarted;

            _closureObserver.Observe(new ReachabilityClosureObservation(
                methodRequests.Count,
                Stopwatch.GetElapsedTime(0, methodPublicationTicks),
                dispatchCount,
                resolvedDispatchCount,
                Stopwatch.GetElapsedTime(0, dispatchPublicationTicks),
                Stopwatch.GetElapsedTime(iterationStarted)));
        }
    }

    // Runs one step for a method with the method's dependency path current. A failure the step raises carries that
    // path, unless it already names its own; one that does is rethrown as it is.
    private void RunOnPath<TState>(ReachabilitySession session, MethodInstanceModel method, TState state,
        Action<ReachabilitySession, TState> step)
    {
        var path = session.State.MethodDependencyPaths[method.CanonicalName];
        var source = new CompilerDiagnosticContext(method.Definition.Key, method.CanonicalName);
        var previousPath = session.CurrentDependencyPath;
        session.CurrentDependencyPath = path.Push(new(source, CompilerDependencyKind.MethodDependency));
        try
        {
            step(session, state);
        }
        catch (CompilerException failure)
        {
            var enriched = _provenance.Bind(failure, source, path);
            if (ReferenceEquals(enriched, failure)) throw;
            throw enriched;
        }
        finally
        {
            session.CurrentDependencyPath = previousPath;
        }
    }
}
