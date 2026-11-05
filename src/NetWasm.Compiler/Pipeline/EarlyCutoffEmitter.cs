using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Pipeline;

/// <summary>A compile the early cutoff may serve: its inputs, metadata and preparation.</summary>
internal sealed record EarlyCutoffRequest(
    CompilerOptions Options,
    MetadataCompilationSnapshot Metadata,
    CompilationPreparation Preparation,
    CompilationInputObservation Inputs);

/// <summary>
/// The last compile's module with the edited methods emitted again, the compile it was kept
/// with and its facts, or why not.
/// </summary>
internal sealed record EarlyCutoffEmission(byte[]? Module, int Patched, string Account)
{
    public UnitKeptCompile? Kept { get; init; }
    public KeptCompilationFacts? Facts { get; init; }
    // The stack-trace symbols, written again, when the compile writes stack traces.
    public StackTraces.StackTraceSymbolArtifact? StackTraceSymbols { get; init; }
}

internal interface IEarlyCutoffEmitter
{
    EarlyCutoffEmission Emit(EarlyCutoffRequest request);
}

/// <summary>
/// The early cutoff: when only bodies changed and every edited method adds to the program
/// what it added before, the program's whole-program state is the last compile's, so only
/// the edited methods' code can differ. Each edited method is analysed alone, its own facts
/// (sites, by-ref effects, exception groups, root map) derived from the last compile's, and
/// it is emitted against the last compile's answers into the last compile's module. Any
/// fact that might differ from what a full compile would derive fails the cutoff.
/// </summary>
internal sealed class EarlyCutoffEmitter(
    IUnitStoreLoader stores,
    IUnitKeptCompileLoader keptCompiles,
    IEditedMethodAnalyzer editedMethods,
    IManagedByRefMethodSummarizerFactory byRefSummarizers,
    IWasmMethodProgramBuilder methodProgram,
    IStructuredProgramInvariantValidator structuredInvariants,
    IRootMapAnalyzerFactory rootMapAnalyzers,
    IReplayMethodEmitterFactory replayEmitters,
    IFunctionIndexResolverFactory functionIndexResolvers,
    ITypeRepositoryFactory typeRepositories,
    IFieldRepositoryFactory fieldRepositories,
    IMethodRepositoryFactory methodRepositories,
    ISymbolFormatterFactory symbolFormatters,
    ITypeClassifierFactory typeClassifiers,
    IRuntimeIntrinsicRegistryFactory intrinsicRegistries,
    ITypeDefinitionResolverFactory typeDefinitionResolvers,
    IExceptionGroupEnumerator exceptionGroups,
    StackTraces.IStackTraceSourceLocationReader sourceLocations,
    StackTraces.IStackTraceSymbolWriter stackTraceSymbols) : IEarlyCutoffEmitter
{
    public EarlyCutoffEmission Emit(EarlyCutoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return Run(request);
        }
        catch (UnitReplayMissException miss)
        {
            return Declined($"the last compile does not answer: {miss.Message}");
        }
        catch (InvalidDataException damaged)
        {
            return Declined($"the kept compile is damaged: {damaged.Message}");
        }
    }

    private EarlyCutoffEmission Run(EarlyCutoffRequest request)
    {
        var (options, metadata, preparation, inputs) = request;
        if (CompilationInputRecorder.CutoffPreconditions(inputs) is { } unmet) return Declined(unmet);
        if (keptCompiles.Load(inputs.Directory) is not { } kept) return Declined("no compile was kept");
        if (KeptCompilationFacts.Read(kept.Facts) is not { } facts) return Declined("the last compile kept no facts");
        if (facts.Options != CompilationOptionsFingerprint.Of(options))
            return Declined("the last compile ran with other options");
        var edited = inputs.Changes.Assemblies
            .SelectMany(assembly => assembly.ChangedBodies.Select(token => new EntityKey(new AssemblyIdentity(assembly.Name), token)))
            .ToArray();
        var types = typeRepositories.Create(metadata);
        var fields = fieldRepositories.Create(metadata);
        var methods = methodRepositories.Create(metadata);
        var symbols = symbolFormatters.Create(metadata);
        if (UnitPlanCodec.Decode(kept.Plans, methods, fields) is not { } plans)
            return Declined("the last compile's plans cannot be replayed");
        if (edited.Length == 0)
        {
            StackTraces.StackTraceSymbolArtifact? unchangedSymbols = null;
            if (options.EmitStackTrace)
            {
                if (plans.StackTraces is not { } numbering0 ||
                    StackTraceLocations(metadata, options, methods, symbols, numbering0, null, []) is not { } table0)
                {
                    return Declined("the stack-trace symbols changed");
                }
                unchangedSymbols = stackTraceSymbols.Write(table0.Symbols);
            }
            return new(kept.Module, 0, "early cutoff: nothing changed\n")
            {
                Kept = kept,
                Facts = facts,
                StackTraceSymbols = unchangedSymbols,
            };
        }
        var functions = UnitFunctionIndices.Read(kept.FunctionIndices);
        if (functions.Functions.Count == 0) return Declined("the last compile kept no function indices");

        // Each edited method, analysed alone, adds to the program what it added before.
        var analysed = editedMethods.Analyze(metadata, preparation, edited, plans.Program.AllocatedTypes);
        if (!analysed.Unanalysed.IsEmpty)
            return Declined($"{analysed.Unanalysed[0]} has no instance of its own to analyse alone");
        var previous = inputs.Previous.Contributions;
        foreach (var method in analysed.Methods)
        {
            var name = method.Method.CanonicalName;
            var contribution = method.Contribution ?? MethodContributionFingerprint.Of(method);
            if (!previous.TryGetValue(name, out var before) || before.Fingerprint != contribution)
                return Declined($"{name} changed what it adds to the program");
        }
        var editedSet = edited.Select(key => (key.Assembly.Name, key.MetadataToken)).ToHashSet();
        if (previous.Values.Count(contribution => editedSet.Contains((contribution.Assembly, contribution.Token))) !=
            analysed.Methods.Length)
        {
            return Declined("an edited method had other instances");
        }

        // Its own facts: call, dispatch and type-test sites, and by-ref effects as before.
        var owners = analysed.Methods.Select(method => method.Method.CanonicalName).ToHashSet(StringComparer.Ordinal);
        var overlay = new Dictionary<(UnitQueryKind Kind, string Key), object>();
        foreach (var method in analysed.Methods)
        {
            foreach (var site in method.Instructions.CallSites.IsDefault ? [] : method.Instructions.CallSites)
            {
                overlay[(UnitQueryKind.ManagedCallSite, $"{site.Key.Caller.CanonicalName}@{site.Key.InstructionOffset:x8}")] =
                    new UnitLookup<Core.IntermediateRepresentation.Calls.ManagedCallSite>(true, site);
            }
        }
        foreach (var (key, site) in analysed.Sites.DispatchCallSites)
            overlay[(UnitQueryKind.DispatchCallSite, key)] = new UnitLookup<DispatchCallSiteModel>(true, site);
        foreach (var (key, site) in analysed.Sites.TypeTestSites)
            overlay[(UnitQueryKind.TypeTestSite, key)] = new UnitLookup<TypeTestSiteModel>(true, site);
        var intrinsics = intrinsicRegistries.Create(symbols, metadata.Methods);
        if (ByRefEffects(methods, intrinsics, plans.Program, analysed, overlay) is { } byRefChange)
            return Declined(byRefChange);
        StackTraces.StackTraceSymbolArtifact? stackTraceArtifact = null;
        if (options.EmitStackTrace)
        {
            if (plans.StackTraces is not { } numbering) return Declined("the last compile kept no stack-trace numbering");
            if (StackTraceLocations(metadata, options, methods, symbols, numbering, analysed, overlay) is not { } table)
                return Declined("the stack-trace symbols changed");
            stackTraceArtifact = stackTraceSymbols.Write(table.Symbols);
        }

        using var store = stores.Load(inputs.Directory);
        var answers = UnitAnswers.Over(store, methods, fields).Overlaid(overlay, owners);
        var typeClassifier = typeClassifiers.Create(metadata);
        var typeDefinitions = typeDefinitionResolvers.Create(metadata);

        // Structured as a full compile structures them, with their exception groups as before.
        var bodies = analysed.Methods.ToDictionary(method => method.Method.Definition.Key, method => method.Body);
        var lowering = methodProgram.Build(bodies, ImmutableDictionary<string, ControlFlow.ManagedMethodBody>.Empty,
            preparation.OptimizationSettings.BranchPrefixDuplication, preparation.OptimizationSettings.DispatcherElimination);
        structuredInvariants.Validate(symbols, lowering);
        foreach (var method in analysed.Methods)
        {
            var name = method.Method.CanonicalName;
            var groups = UnitExceptionGroups.Of(lowering.Methods[method.Method.Definition.Key],
                method.Body.Body.ExceptionRegions, exceptionGroups);
            if (groups is null) return Declined($"{name} has an exception filter");
            if (store.Find(name) is not { } unit) return Declined($"{name} has no unit to compare its exception groups with");
            var record = store.Segments[unit.Segment]!.Record(unit.Offset);
            if (!record.HasExceptionGroups || !SameGroups(record.ExceptionGroups(), groups.Value))
                return Declined($"{name} changed its exception groups");
        }

        // Root maps from the last compile's facts and the methods' own sites.
        var replay = new UnitReplay(answers, plans,
            functionIndexResolvers.Create(methods, symbols, plans.Target.FunctionIndices));
        var rootMapAnalyzer = rootMapAnalyzers.Create(types, fields, methods, replay.Values,
            new ReplayDictionary<string, DispatchCallSiteModel>(answers, UnitQueryKind.DispatchCallSite, static key => key),
            typeClassifier);
        var allocating = new ReplayMembershipSet<EntityKey>(answers, UnitQueryKind.AllocatingMethod, UnitQueryKeys.Of);
        var allocatingConstructed = new ReplayMembershipSet<string>(answers, UnitQueryKind.AllocatingConstructedMethod,
            static key => key);
        var rootMaps = new Dictionary<string, MethodRootMap>(StringComparer.Ordinal);
        foreach (var method in analysed.Methods)
        {
            var map = rootMapAnalyzer.Analyze(new RootMapAnalysisRequest(method.Body, allocating, allocatingConstructed));
            rootMaps[method.Method.CanonicalName] = map;
            overlay[(UnitQueryKind.RootMap, Wasm.Emission.Methods.ManagedMethodBodyKey.Resolve(
                lowering.Methods[method.Method.Definition.Key]))] = new UnitLookup<MethodRootMap>(true, map);
        }

        // Emitted against the last compile, into its module.
        using var emitter = replayEmitters.Create(replay, types, typeDefinitions, fields, methods, symbols, typeClassifier,
            intrinsics, options.Target);
        var patched = new Dictionary<int, byte[]>();
        foreach (var method in analysed.Methods)
        {
            var name = method.Method.CanonicalName;
            if (!functions.Functions.TryGetValue(name, out var index))
                return Declined($"{name} has no function in the last compile's module");
            var function = emitter.Emit(new ManagedMethodIdentity(name), method.Method.Definition.Key,
                lowering.Methods[method.Method.Definition.Key], rootMaps[name]);
            patched[index - functions.ImportedFunctions] = function.Body;
        }
        var module = WasmCodePatcher.ReplaceBodies(kept.Module, patched);
        return module is null
            ? Declined("the last compile's module cannot be patched")
            : new(module, patched.Count, string.Create(CultureInfo.InvariantCulture,
                $"early cutoff: patched {patched.Count} of the last compile's functions\n"))
            {
                Kept = kept,
                Facts = facts,
                StackTraceSymbols = stackTraceArtifact,
            };
    }

    // The stack-trace symbols the last compile's methods take from the current sources, with
    // each edited method's own locations in front; none when any symbol, or which symbols a
    // method's locations take, differs from the last compile's: an edit may move only where
    // in its own body its locations start.
    private StackTraceSymbolTable? StackTraceLocations(MetadataCompilationSnapshot metadata, CompilerOptions options,
        IMethodRepository methods, ISymbolFormatter symbols, UnitStackTraces numbering, EditedMethods? analysed,
        Dictionary<(UnitQueryKind Kind, string Key), object> overlay)
    {
        var table = StackTraceSymbolTable.Build(
            [.. numbering.DirectMethodIds.OrderBy(pair => pair.Value)
                .Select(pair => (pair.Key, symbols.Format(methods.GetMethod(pair.Key))))],
            [.. numbering.ConstructedMethodIds.OrderBy(pair => pair.Value)
                .Select(pair => new StackTraceConstructedMethod(pair.Key, numbering.ConstructedDefinitions[pair.Key]))],
            sourceLocations.Read(metadata, options));
        if (StackTraceSymbolTable.Fingerprint(table.Symbols, table.LocationSymbols) != numbering.Table) return null;
        foreach (var method in analysed?.Methods ?? [])
        {
            if (!numbering.DirectMethodIds.TryGetValue(method.Method.Definition.Key, out var id)) continue;
            var found = table.LocationSymbols.TryGetValue(id, out var locations);
            overlay[(UnitQueryKind.StackTraceLocationSymbols, id.ToString(CultureInfo.InvariantCulture))] =
                new UnitLookup<ImmutableArray<StackTraceLocationSymbol>>(found, locations);
        }
        return table;
    }

    // Why an edited method's by-ref effects may differ from the last compile's: they read
    // their own through a cycle, or come out other than before; null when they are the same.
    private string? ByRefEffects(IMethodRepository methods, IRuntimeIntrinsicRegistry intrinsics, UnitProgramFacts program,
        EditedMethods analysed, Dictionary<(UnitQueryKind Kind, string Key), object> overlay)
    {
        var summarizer = byRefSummarizers.Create(methods, intrinsics);
        var identities = program.DirectMethods.Values.Concat(program.ConstructedMethods).ToHashSet(StringComparer.Ordinal);
        var sites = analysed.Sites.DispatchCallSites;
        foreach (var method in analysed.Methods)
        {
            var name = method.Method.CanonicalName;
            var summary = summarizer.Summarize(new(name, method.Body, identities, program.DirectMethods,
                program.ByRefEffects, sites));
            var before = program.ByRefEffects.GetValueOrDefault(name) ?? ManagedByRefMethodEffects.Empty;
            if (!summary.Effects.WrittenArguments.SetEquals(before.WrittenArguments) ||
                !summary.Effects.EscapedArguments.SetEquals(before.EscapedArguments) ||
                !summary.Effects.ReturnedArgumentAliases.SetEquals(before.ReturnedArgumentAliases))
            {
                return $"{name} changed its by-ref effects";
            }
            if (Reaches(summary.Callees, name, program.ByRefCallees))
                return $"{name} reads its by-ref effects back through a call cycle";
            overlay[(UnitQueryKind.ByRefEffects, name)] = new UnitLookup<ManagedByRefMethodEffects>(true, summary.Effects);
        }
        return null;
    }

    private static bool Reaches(ImmutableArray<string> from, string target,
        IReadOnlyDictionary<string, ImmutableArray<string>> callees)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(from);
        while (pending.TryPop(out var next))
        {
            if (string.Equals(next, target, StringComparison.Ordinal)) return true;
            if (!seen.Add(next) || !callees.TryGetValue(next, out var onward)) continue;
            foreach (var callee in onward) pending.Push(callee);
        }
        return false;
    }

    // Exception metadata is laid out per group and clause; offsets inside the body may move.
    private static bool SameGroups(ImmutableArray<UnitExceptionGroup> before, ImmutableArray<UnitExceptionGroup> now) =>
        before.Length == now.Length && before.Zip(now).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Parent == pair.Second.Parent &&
            pair.First.Regions.SequenceEqual(pair.Second.Regions));

    private static EarlyCutoffEmission Declined(string reason) => new(null, 0, $"early cutoff: declined ({reason})\n");
}

internal interface IManagedByRefMethodSummarizerFactory
{
    IManagedByRefMethodSummarizer Create(IMethodRepository methods, IRuntimeIntrinsicRegistry intrinsics);
}

internal sealed class ManagedByRefMethodSummarizerFactory(IIndexedWorkExecutor work) : IManagedByRefMethodSummarizerFactory
{
    public IManagedByRefMethodSummarizer Create(IMethodRepository methods, IRuntimeIntrinsicRegistry intrinsics) =>
        new ManagedByRefMethodSummarizer(methods, intrinsics, work);
}
