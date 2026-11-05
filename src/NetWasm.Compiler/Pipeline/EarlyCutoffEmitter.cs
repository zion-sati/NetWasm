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
    public static EarlyCutoffEmission Declined(string reason) => new(null, 0, $"early cutoff: declined ({reason})\n");

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
/// The early cutoff as the pipeline asks it: a last compile that does not answer what the
/// edited methods ask, or whose kept data is damaged, declines the cutoff rather than failing
/// the compile, which then runs in full.
/// </summary>
internal sealed class DecliningEarlyCutoffEmitter(IEarlyCutoffEmitter inner) : IEarlyCutoffEmitter
{
    private readonly IEarlyCutoffEmitter _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public EarlyCutoffEmission Emit(EarlyCutoffRequest request)
    {
        try
        {
            return _inner.Emit(request);
        }
        catch (UnitReplayMissException miss)
        {
            return EarlyCutoffEmission.Declined($"the last compile does not answer: {miss.Message}");
        }
        catch (InvalidDataException damaged)
        {
            return EarlyCutoffEmission.Declined($"the kept compile is damaged: {damaged.Message}");
        }
    }
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
        EarlyCutoffStackTraces? stackTraces = null;
        StackTraces.StackTraceSymbolArtifact? stackTraceArtifact = null;
        if (options.EmitStackTrace)
        {
            stackTraces = EarlyCutoffStackTraces.Replay(plans.StackTraces, methods, symbols, sourceLocations.Read(metadata, options),
                edited);
            if (stackTraces is null) return Declined("the stack-trace symbols changed");
            stackTraceArtifact = stackTraceSymbols.Write(stackTraces.Table.Symbols);
        }
        if (edited.Length == 0)
        {
            return new(kept.Module, 0, "early cutoff: nothing changed\n")
            {
                Kept = kept,
                Facts = facts,
                StackTraceSymbols = stackTraceArtifact,
            };
        }
        var functions = UnitFunctionIndices.Read(kept.FunctionIndices);
        if (functions.Functions.Count == 0) return Declined("the last compile kept no function indices");

        // Each edited method, analysed alone, adds to the program what it added before.
        var analysed = editedMethods.Analyze(metadata, preparation, edited, plans.Program.AllocatedTypes);
        if (!analysed.Unanalysed.IsEmpty)
            return Declined($"{analysed.Unanalysed[0]} has no instance of its own to analyse alone");
        var contributions = analysed.Methods.ToDictionary(method => method.Method.CanonicalName, method => new MethodContributionRecord(
            method.Method.Definition.Key.Assembly.Name, method.Method.Definition.Key.MetadataToken, method.Contribution),
            StringComparer.Ordinal);
        if (EditedContributions.Compare(EditedContributions.Bodies(inputs.Changes), inputs.Previous.Contributions, contributions)
            .Obstacle is { } obstacle)
        {
            return Declined(obstacle);
        }

        // Its own facts: call, dispatch and type-test sites, and by-ref effects as before.
        var owners = analysed.Methods.Select(method => method.Method.CanonicalName).ToHashSet(StringComparer.Ordinal);
        var overlay = new Dictionary<(UnitQueryKind Kind, string Key), object>();
        foreach (var method in analysed.Methods)
        {
            foreach (var site in method.Instructions.CallSites)
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
        foreach (var (id, locations) in stackTraces?.EditedLocations ?? [])
            overlay[(UnitQueryKind.StackTraceLocationSymbols, id.ToString(CultureInfo.InvariantCulture))] = locations;

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
            if (!record.HasExceptionGroups || !UnitExceptionGroups.Same(record.ExceptionGroups(), groups.Value))
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
            if (ByRefCallGraph.Reaches(summary.Callees, name, program.ByRefCallees))
                return $"{name} reads its by-ref effects back through a call cycle";
            overlay[(UnitQueryKind.ByRefEffects, name)] = new UnitLookup<ManagedByRefMethodEffects>(true, summary.Effects);
        }
        return null;
    }

    private static EarlyCutoffEmission Declined(string reason) => EarlyCutoffEmission.Declined(reason);
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
