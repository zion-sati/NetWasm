using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompiledUnitStateFactory
{
    // This compile's compiled units over the store the last compile kept; null when units are off.
    CompiledUnitState? Create(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        CompilationInputObservation? inputs,
        ReachableProgram program,
        BranchPrefixDuplicationPolicy branchPrefixDuplication,
        DispatcherEliminationPolicy dispatcherElimination);
}

internal sealed class CompiledUnitStateFactory(
    IUnitStoreLoader loader,
    IUnitKeptCompileLoader keptCompiles,
    IWasmMethodLowerer structuring,
    ICompilationInputReader inputReader,
    IMethodRepositoryFactory methodRepositories,
    IFieldRepositoryFactory fieldRepositories,
    ISymbolFormatterFactory symbolFormatters,
    IFunctionIndexResolverFactory functionIndexResolvers) : ICompiledUnitStateFactory
{
    private readonly IUnitStoreLoader _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    private readonly IWasmMethodLowerer _structuring = structuring ??
        throw new ArgumentNullException(nameof(structuring));

    public CompiledUnitState? Create(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        CompilationInputObservation? inputs,
        ReachableProgram program,
        BranchPrefixDuplicationPolicy branchPrefixDuplication,
        DispatcherEliminationPolicy dispatcherElimination)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(program);
        if (CompiledUnitPolicy.DirectoryFor(options) is not { } directory)
            return null;
        var state = new CompiledUnitState(directory)
        {
            Verifies = options.VerifyCompiledUnits,
            // Every unit's input holds what emission reads from the compile itself rather than
            // through a recorded query: the target's width and how methods are structured.
            Policy = $"{options.Target}/{branchPrefixDuplication}/{dispatcherElimination}",
            Bodies = CompiledUnitState.Index(program.Methods.Values, program.ConstructedMethods.Values),
            Store = _loader.Load(directory),
            Structuring = _structuring,
            // Units depend on assembly shapes; without detection, they are read here.
            AssemblyShapes = AssemblyShapes(options, metadata,
                inputs?.Current ?? inputReader.Read(metadata, CompilationInputs.Empty)),
        };
        // Verification proves the early cutoff's replay too, where the cutoff applies.
        if (options.VerifyCompiledUnits && inputs is not null &&
            CompilationInputRecorder.CutoffVerdict(inputs, CompilationInputRecorder.WithContributions(inputs.Current, program)).Applies)
        {
            state.Replay = Replay(directory, metadata, state.Store);
        }
        return state;
    }

    // The last compile to replay against, or none when it was not kept or cannot be replayed.
    private UnitReplay? Replay(string directory, MetadataCompilationSnapshot metadata, UnitStore store)
    {
        if (keptCompiles.Load(directory) is not { } kept) return null;
        var methods = methodRepositories.Create(metadata);
        var fields = fieldRepositories.Create(metadata);
        try
        {
            if (UnitPlanCodec.Decode(kept.Plans, methods, fields) is not { } plans) return null;
            return new UnitReplay(UnitAnswers.Over(store, methods, fields), plans,
                functionIndexResolvers.Create(methods, symbolFormatters.Create(metadata), plans.Target.FunctionIndices));
        }
        catch (System.IO.InvalidDataException)
        {
            // A damaged store or kept compile costs the replay, never the compile.
            return null;
        }
    }

    // Each assembly's shape together with those of everything it references, transitively,
    // and of the core assemblies every program builds on.
    private static FrozenDictionary<string, UnitFingerprint> AssemblyShapes(
        CompilerOptions options, MetadataCompilationSnapshot metadata, CompilationInputs inputs)
    {
        var shapes = new Dictionary<string, InputFingerprint>(StringComparer.Ordinal);
        foreach (var assembly in inputs.Assemblies)
            shapes[assembly.Name] = assembly.Detail?.Shape ?? assembly.Content;
        var byName = metadata.Assemblies.ToDictionary(assembly => assembly.Identity.Name, StringComparer.Ordinal);
        string Resolve(string reference) =>
            options.ReferenceAssemblyAliases?.TryGetValue(reference, out var target) == true ? target : reference;
        var cores = metadata.Assemblies
            .Where(assembly => !assembly.References.Any(reference => byName.ContainsKey(Resolve(reference))))
            .Select(assembly => assembly.Identity.Name)
            .ToArray();
        var result = new Dictionary<string, UnitFingerprint>(byName.Count, StringComparer.Ordinal);
        foreach (var name in byName.Keys)
        {
            var closure = new SortedSet<string>(cores, StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(name);
            while (pending.TryPop(out var next))
            {
                if (!closure.Add(next) && next != name) continue;
                foreach (var reference in byName[next].References)
                {
                    var resolved = Resolve(reference);
                    if (byName.ContainsKey(resolved) && !closure.Contains(resolved)) pending.Push(resolved);
                }
            }
            var fingerprint = new UnitFingerprintBuilder();
            fingerprint.Add(closure.Count);
            foreach (var member in closure)
            {
                fingerprint.Add(member);
                var shape = shapes.GetValueOrDefault(member);
                fingerprint.Add((long)shape.First);
                fingerprint.Add((long)shape.Second);
            }
            result[name] = fingerprint.Finish();
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
