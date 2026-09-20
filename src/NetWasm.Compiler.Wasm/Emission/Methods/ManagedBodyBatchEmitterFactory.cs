using System;
using System.Collections.Generic;
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
        var built = new List<ManagedBodyWorker>(forkSet.Workers.Length);
        try
        {
            foreach (var fork in forkSet.Workers)
                built.Add(workers.Create(fork));
            return new ManagedBodyBatchEmitter(forkSet, [.. built]);
        }
        catch
        {
            foreach (var worker in built)
                worker.Dispose();
            throw;
        }
    }
}
