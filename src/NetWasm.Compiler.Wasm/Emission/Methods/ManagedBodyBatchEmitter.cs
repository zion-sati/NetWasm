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
using NetWasm.Compiler.Wasm.Units;

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
        var restored = worker.Restorer?.Restore(method.Identity.CanonicalName,
            method.MethodKey.ToString(), methods[method.MethodKey], rootMaps[method.MethodKey], target);
        if (restored is not null && !worker.VerifiesUnits) return restored;
        var functions = new List<WasmFunctionDefinition>(1);
        var environments = new Dictionary<string, FilterEnvironmentLayout>(
            1, StringComparer.Ordinal);
        var emissions = new List<ManagedMethodEmissionRecord>(1);
        var log = Begin(worker);
        worker.Ordinary.Append(functions, environments, emissions,
            method.Identity, method.MethodKey, methods[method.MethodKey],
            rootMaps[method.MethodKey], View(target, log), Indices(indices, log));
        var environment = environments.Single();
        var emitted = End(log, new(functions.Single(), environment.Key, environment.Value, emissions.Single()));
        Replayed(worker, index, method.Identity.CanonicalName, emitted, (replay, unitReplay, functions, environments, emissions) =>
            replay.Ordinary.Append(functions, environments, emissions, method.Identity, method.MethodKey,
                methods[method.MethodKey], rootMaps[method.MethodKey], unitReplay.Target, unitReplay.Indices));
        return Verified(method.Identity.CanonicalName, restored, emitted);
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
        var restored = worker.Restorer?.Restore(key, key, methods[key], rootMaps[key], target);
        if (restored is not null && !worker.VerifiesUnits) return restored;
        var functions = new List<WasmFunctionDefinition>(1);
        var environments = new Dictionary<string, FilterEnvironmentLayout>(
            1, StringComparer.Ordinal);
        var emissions = new List<ManagedMethodEmissionRecord>(1);
        var log = Begin(worker);
        worker.Constructed.Append(functions, environments, emissions,
            identity, instances[key], methods[key], rootMaps[key],
            View(target, log), Indices(indices, log));
        var environment = environments.Single();
        var emitted = End(log, new(functions.Single(), environment.Key, environment.Value, emissions.Single()));
        Replayed(worker, index, key, emitted, (replay, unitReplay, functions, environments, emissions) =>
            replay.Constructed.Append(functions, environments, emissions, identity, instances[key], methods[key],
                rootMaps[key], unitReplay.Target, unitReplay.Indices));
        return Verified(key, restored, emitted);
    });

    private delegate void ReplayAppend(ManagedBodyWorker replay, UnitReplay unitReplay,
        List<WasmFunctionDefinition> functions, Dictionary<string, FilterEnvironmentLayout> environments,
        List<ManagedMethodEmissionRecord> emissions);

    // Under verification with the early cutoff applying, a method emitted against the last
    // compile's answers must get the body emission gave it; a query the answers do not hold
    // only means the method cannot be replayed.
    private static void Replayed(ManagedBodyWorker worker, int index, string identity, ManagedBodyEmission emitted,
        ReplayAppend append)
    {
        if (worker.Replay is not { Worker: var replay, Units: var unitReplay }) return;
        var functions = new List<WasmFunctionDefinition>(1);
        var environments = new Dictionary<string, FilterEnvironmentLayout>(1, StringComparer.Ordinal);
        var emissions = new List<ManagedMethodEmissionRecord>(1);
        replay.Logger.BeginMethod(index);
        try
        {
            append(replay, unitReplay, functions, environments, emissions);
        }
        catch (UnitReplayMissException miss)
        {
            unitReplay.Missed(miss);
            return;
        }
        finally
        {
            replay.Logger.EndMethod();
        }
        var body = functions.Single().Body;
        if (!body.AsSpan().SequenceEqual(emitted.Function.Body))
        {
            var at = body.AsSpan().CommonPrefixLength(emitted.Function.Body);
            throw new InvalidOperationException(
                $"Replaying {identity} against the last compile gave a body that differs from emission at byte {at}.");
        }
        unitReplay.Replayed();
    }

    // A restored body must be the one emission writes; under verification both are made
    // and any difference fails the compile.
    private static ManagedBodyEmission Verified(string identity, ManagedBodyEmission? restored,
        ManagedBodyEmission emitted)
    {
        if (restored is null)
            return emitted;
        var difference =
            restored.Function.Name != emitted.Function.Name ? "name" :
            !restored.Function.Type.Parameters.SequenceEqual(emitted.Function.Type.Parameters) ||
                restored.Function.Type.Result != emitted.Function.Type.Result ? "type" :
            !restored.Function.Body.AsSpan().SequenceEqual(emitted.Function.Body) ? "body" :
            restored.Function.IsAggressiveInlining != emitted.Function.IsAggressiveInlining ||
                restored.Function.IsNoInlining != emitted.Function.IsNoInlining ? "inlining" :
            restored.EnvironmentKey != emitted.EnvironmentKey ? "environment key" :
            restored.Record.Emission.WasmInstructionCount != emitted.Record.Emission.WasmInstructionCount
                ? "instruction count" :
            !restored.Record.Emission.OriginalBlockEmissionCounts.OrderBy(pair => pair.Key)
                .SequenceEqual(emitted.Record.Emission.OriginalBlockEmissionCounts.OrderBy(pair => pair.Key))
                ? "block counts" :
            null;
        if (difference is null)
            return restored;
        var at = restored.Function.Body.AsSpan().CommonPrefixLength(emitted.Function.Body);
        static string Window(byte[] body, int at) =>
            Convert.ToHexString(body.AsSpan(Math.Max(0, at - 8), Math.Min(24, body.Length - Math.Max(0, at - 8))));
        throw new InvalidOperationException(
            $"The compiled unit of {identity} restored a {difference} that differs from emission" +
            (difference == "body"
                ? $" at byte {at}: restored {Window(restored.Function.Body, at)}, emitted {Window(emitted.Function.Body, at)}."
                : "."));
    }

    // With compiled units on, a method is emitted against a view of the target that
    // records what it looks up, and its emission carries those queries.
    private static UnitDependencyLog? Begin(ManagedBodyWorker worker)
    {
        worker.Dependencies?.Begin();
        return worker.Dependencies;
    }

    private static InstructionModuleTarget View(InstructionModuleTarget target, UnitDependencyLog? log) =>
        log is null ? target : UnitTargetViews.Recording(target, log);

    private static IFunctionIndexResolver Indices(IFunctionIndexResolver indices, UnitDependencyLog? log) =>
        log is null ? indices : UnitTargetViews.Recording(indices, log);

    private static ManagedBodyEmission End(UnitDependencyLog? log, ManagedBodyEmission emission) =>
        log is null ? emission : emission with { Dependencies = log.End() };

    private ManagedBodyEmission[] Run(int count,
        Func<ManagedBodyWorker, int, ManagedBodyEmission> emit)
    {
        var results = new ManagedBodyEmission[count];
        var owners = new ManagedBodyWorker?[count];
        var failures = new ExceptionDispatchInfo?[count];
        var next = -1;
        void Work(ManagedBodyWorker worker)
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
        }
        // One worker works on the calling thread, as hosts without threads require.
        if (workers.Length == 1 || count <= 1)
        {
            Work(workers[0]);
        }
        else
        {
            var tasks = new Task[Math.Min(workers.Length, count)];
            for (var workerIndex = 0; workerIndex < tasks.Length; workerIndex++)
            {
                var worker = workers[workerIndex];
                tasks[workerIndex] = Task.Run(() => Work(worker));
            }
            Task.WaitAll(tasks);
        }
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
