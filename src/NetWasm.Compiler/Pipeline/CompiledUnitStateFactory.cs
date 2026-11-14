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
    // This compile's compiled units over the store the last compile kept; null when units are off,
    // which is when no inputs were detected.
    // Under verification, they replay every method against the kept compile the early cutoff
    // served this compile from, when it served it.
    CompiledUnitState? Create(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        CompilationInputObservation? inputs,
        ReachableProgram program,
        BranchPrefixDuplicationPolicy branchPrefixDuplication,
        DispatcherEliminationPolicy dispatcherElimination,
        UnitKeptCompile? servedFrom);
}

internal sealed class CompiledUnitStateFactory(
    IUnitStoreLoader loader,
    IWasmMethodLowerer structuring,
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
        DispatcherEliminationPolicy dispatcherElimination,
        UnitKeptCompile? servedFrom)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(program);
        if (inputs is null)
            return null;
        var state = new CompiledUnitState(inputs.Directory)
        {
            Verifies = options.VerifyCompiledUnits,
            // Every unit's input holds what emission reads from the compile itself rather than
            // through a recorded query: the target's width and how methods are structured.
            // and which native libraries bind, which decides whether a P/Invoke is an import or a throwing body.
            Policy = $"{options.Target}/{branchPrefixDuplication}/{dispatcherElimination}/{NativeBindingPolicy(options)}" +
                $"/{FeatureSwitchPolicy(options)}",
            Bodies = CompiledUnitState.Index(program.Methods.Values, program.ConstructedMethods.Values),
            Store = _loader.Load(inputs.Directory),
            Structuring = _structuring,
            AssemblyShapes = UnitAssemblyShapes.Of(
                metadata.Assemblies.ToDictionary(assembly => assembly.Identity.Name, assembly => assembly.References,
                    StringComparer.Ordinal),
                options.ReferenceAssemblyAliases,
                inputs.Current.Assemblies.ToDictionary(assembly => assembly.Name, assembly => assembly.Shape, StringComparer.Ordinal)),
        };
        // Verification proves the early cutoff's replay too, where the cutoff served the compile:
        // it serves one only from a compile with the same options, whose answers then hold.
        if (options.VerifyCompiledUnits && servedFrom is not null)
        {
            state.Replay = Replay(servedFrom, metadata, state.Store);
        }
        return state;
    }

    // The compile the cutoff served this one from, to replay against: the cutoff read its
    // plans to serve it, so they read again. A cutoff with nothing changed reads no units, so
    // the store may be damaged; that costs the replay, never the compile.
    private UnitReplay? Replay(UnitKeptCompile kept, MetadataCompilationSnapshot metadata, UnitStore store)
    {
        var methods = methodRepositories.Create(metadata);
        var fields = fieldRepositories.Create(metadata);
        var plans = UnitPlanCodec.Decode(kept.Plans, methods, fields)!;
        try
        {
            return new UnitReplay(UnitAnswers.Over(store, methods, fields), plans,
                functionIndexResolvers.Create(methods, symbolFormatters.Create(metadata), plans.Target.FunctionIndices));
        }
        catch (System.IO.InvalidDataException)
        {
            return null;
        }
    }

    // Folded feature switches change the bodies that call their getters.
    // and the runtime properties AppContext's ConfigurationProperties returns.
    internal static string FeatureSwitchPolicy(CompilerOptions options) =>
        "switches:" + string.Join(",", options.FeatureSwitches.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")) +
        ";properties:" + string.Join(",", options.RuntimeProperties.Select(pair => $"{pair.Name}={pair.Value}"));

    // The units of a compile that links every native library never serve one that links some, nor the reverse.
    internal static string NativeBindingPolicy(CompilerOptions options) =>
        options.LinkedNativeLibraries is { } linked
            ? "native:" + string.Join(",", linked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            : "native:all";
}
