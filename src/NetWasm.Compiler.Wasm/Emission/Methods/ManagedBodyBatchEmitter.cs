using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedBodyBatchEmitter(
    ManagedLayoutForks forks,
    ManagedBodyWorker[] workers) : IManagedBodyBatchEmitter
{
    public ManagedBodyEmission[] Emit(
        ImmutableArray<ManagedDefinitionEmission> ordered,
        IReadOnlyDictionary<EntityKey, StructuredMethod> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        InstructionModuleTarget target,
        IFunctionIndexResolver indices) => Run(ordered.Length, (worker, index) =>
    {
        var method = ordered[index];
        var functions = new List<WasmFunctionDefinition>(1);
        var environments = new Dictionary<string, FilterEnvironmentLayout>(
            1, StringComparer.Ordinal);
        var emissions = new List<ManagedMethodEmissionRecord>(1);
        worker.Ordinary.Append(functions, environments, emissions,
            method.Identity, method.MethodKey, methods[method.MethodKey],
            rootMaps[method.MethodKey], target, indices);
        var environment = environments.Single();
        return new(functions.Single(), environment.Key,
            environment.Value, emissions.Single());
    });

    public ManagedBodyEmission[] Emit(
        ImmutableArray<ManagedMethodIdentity> ordered,
        IReadOnlyDictionary<string, MethodInstanceModel> instances,
        IReadOnlyDictionary<string, StructuredMethod> methods,
        IReadOnlyDictionary<string, MethodRootMap> rootMaps,
        InstructionModuleTarget target,
        IFunctionIndexResolver indices) => Run(ordered.Length, (worker, index) =>
    {
        var identity = ordered[index];
        var key = identity.CanonicalName;
        var functions = new List<WasmFunctionDefinition>(1);
        var environments = new Dictionary<string, FilterEnvironmentLayout>(
            1, StringComparer.Ordinal);
        var emissions = new List<ManagedMethodEmissionRecord>(1);
        worker.Constructed.Append(functions, environments, emissions,
            identity, instances[key], methods[key], rootMaps[key],
            target, indices);
        var environment = environments.Single();
        return new(functions.Single(), environment.Key,
            environment.Value, emissions.Single());
    });

    private ManagedBodyEmission[] Run(int count,
        Func<ManagedBodyWorker, int, ManagedBodyEmission> emit)
    {
        var results = new ManagedBodyEmission[count];
        var owners = new ManagedBodyWorker?[count];
        var failures = new ExceptionDispatchInfo?[count];
        var next = -1;
        var tasks = new Task[Math.Min(workers.Length, count)];
        for (var workerIndex = 0; workerIndex < tasks.Length; workerIndex++)
        {
            var worker = workers[workerIndex];
            tasks[workerIndex] = Task.Run(() =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= count) return;
                    owners[index] = worker;
                    try
                    {
                        worker.Logger.BeginMethod(index);
                        try { results[index] = emit(worker, index); }
                        finally { worker.Logger.EndMethod(); }
                    }
                    catch (Exception exception)
                    {
                        failures[index] = ExceptionDispatchInfo.Capture(
                            exception);
                    }
                }
            });
        }

        Task.WaitAll(tasks);
        var failedIndex = Array.FindIndex(failures,
            failure => failure is not null);
        var logLimit = failedIndex < 0 ? count : failedIndex + 1;
        for (var index = 0; index < logLimit; index++)
            owners[index]!.Logger.Replay(index);
        if (failedIndex >= 0)
            failures[failedIndex]!.Throw();
        forks.Publisher.Publish();
        return results;
    }

    public void Dispose()
    {
        foreach (var worker in workers)
            worker.Dispose();
    }
}
