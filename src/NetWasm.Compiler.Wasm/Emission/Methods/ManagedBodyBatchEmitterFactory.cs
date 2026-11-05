using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedBodyBatchEmitterFactory(
    IManagedLayoutForkSource forks,
    int workerCount,
    IManagedBodyWorkerFactory workers) : IManagedBodyBatchEmitterFactory
{
    public IManagedBodyBatchEmitter Create()
    {
        var forkSet = forks.Create(workerCount);
        // Each worker composes its own services; they are built side by side. If any fails,
        // the others are disposed and the first failure in worker order is thrown.
        var built = new ManagedBodyWorker?[forkSet.Workers.Length];
        var failures = new System.Runtime.ExceptionServices.ExceptionDispatchInfo?[built.Length];
        void Build(int index)
        {
            try
            {
                built[index] = workers.Create(forkSet.Workers[index]);
            }
#pragma warning disable CA1031 // Rethrown below, in worker order.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failures[index] = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            }
        }
        // One worker is built on the calling thread, as hosts without threads require.
        if (built.Length == 1)
            Build(0);
        else
            System.Threading.Tasks.Parallel.For(0, built.Length, Build);
        if (Array.Find(failures, failure => failure is not null) is { } first)
        {
            foreach (var worker in built) worker?.Dispose();
            first.Throw();
        }
        return new ManagedBodyBatchEmitter(forkSet, [.. built.Select(worker => worker!)]);
    }
}
