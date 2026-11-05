using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// Lowers every reachable method to structured control flow, or to an outline where the
// outline provider defers it. Each method lowers on its own, so the methods are shared
// among the compiler's workers; with one worker, as in the browser, they lower in turn on
// the calling thread.
internal sealed class WasmMethodProgramBuilder(
    IWasmMethodLowerer methods,
    IIndexedWorkExecutor work,
    CompilerParallelism parallelism) : IWasmMethodProgramBuilder
{
    private readonly IWasmMethodLowerer _methods = methods ??
        throw new ArgumentNullException(nameof(methods));
    private readonly IIndexedWorkExecutor _work = work ??
        throw new ArgumentNullException(nameof(work));
    private readonly CompilerParallelism _parallelism = parallelism ??
        throw new ArgumentNullException(nameof(parallelism));

    public WasmMethodLoweringResult Build(
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> methods,
        IReadOnlyDictionary<string, ManagedMethodBody> constructedMethods,
        BranchPrefixDuplicationPolicy branchPrefixDuplication =
            BranchPrefixDuplicationPolicy.SizeAware,
        DispatcherEliminationPolicy dispatcherElimination =
            DispatcherEliminationPolicy.Disabled,
        IMethodOutlineProvider? outlines = null)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(constructedMethods);
        var ordinary = methods.ToArray();
        var constructed = constructedMethods.ToArray();
        var bodies = new ManagedMethodBody[ordinary.Length + constructed.Length];
        for (var index = 0; index < ordinary.Length; index++)
        {
            bodies[index] = ordinary[index].Value;
        }
        for (var index = 0; index < constructed.Length; index++)
        {
            bodies[ordinary.Length + index] = constructed[index].Value;
        }

        StructuredMethod[] lowered;
        if (_parallelism.WorkerCount == 1 || bodies.Length < 2)
        {
            lowered = new StructuredMethod[bodies.Length];
            for (var index = 0; index < bodies.Length; index++)
            {
                lowered[index] = outlines?.Outline(bodies[index]) ??
                    _methods.Lower(bodies[index], branchPrefixDuplication, dispatcherElimination);
            }
        }
        else
        {
            // Results come back in request order, and the first failure in that order is the
            // one thrown, as when the methods lowered in turn.
            var workers = new IWasmMethodLowerer[Math.Min(_parallelism.WorkerCount, bodies.Length)];
            Array.Fill(workers, _methods);
            lowered = _work.Execute(workers, bodies.Length,
                (lowerer, index) => outlines?.Outline(bodies[index]) ??
                    lowerer.Lower(bodies[index], branchPrefixDuplication, dispatcherElimination));
        }

        var ordinaryLowered = ImmutableDictionary.CreateBuilder<EntityKey, StructuredMethod>();
        for (var index = 0; index < ordinary.Length; index++)
        {
            ordinaryLowered.Add(ordinary[index].Key, lowered[index]);
        }
        var constructedLowered = ImmutableDictionary.CreateBuilder<string, StructuredMethod>(StringComparer.Ordinal);
        for (var index = 0; index < constructed.Length; index++)
        {
            constructedLowered.Add(constructed[index].Key, lowered[ordinary.Length + index]);
        }
        return new(ordinaryLowered.ToImmutable(), constructedLowered.ToImmutable());
    }
}
