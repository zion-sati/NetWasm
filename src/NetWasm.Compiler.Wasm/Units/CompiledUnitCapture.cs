using NetWasm.Compiler.ControlFlow;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// One compile's compiled units: where they are kept, the last compile's store, the
/// module's function symbols once planned, and what this compile restored and emitted.
/// Units are off without a directory. Disposing the state releases the store.
/// </summary>
internal sealed class CompiledUnitState(string? directory) : IDisposable
{
    private readonly ConcurrentDictionary<string, (MethodRootMap RootMap, UnitFingerprint? Input)> _inputs =
        new(StringComparer.Ordinal);

    private WasmFunctionSymbols _symbols = WasmFunctionSymbols.Empty;
    private FrozenDictionary<string, uint>.AlternateLookup<ReadOnlySpan<char>> _bySymbol;
    private bool _hasSpanLookup;

    internal string? Directory { get; } = directory;
    // Whether restored methods are emitted as well and compared.
    internal bool Verifies { get; init; }
    // The target and how methods are structured; part of every unit's input.
    internal string Policy { get; init; } = "";
    // By assembly name, a fingerprint of the shapes of the assembly and every assembly it
    // references, directly or not: a unit depends on those of each assembly its method names.
    internal FrozenDictionary<string, UnitFingerprint> AssemblyShapes { get; init; } =
        FrozenDictionary<string, UnitFingerprint>.Empty;
    // The pipeline's structuring, for outlines whose units turn out stale.
    internal IWasmMethodLowerer? Structuring { get; init; }
    // The analysed bodies by method identity: a unit's input is its method's analysed body.
    internal FrozenDictionary<string, ManagedMethodBody> Bodies { get; init; } =
        FrozenDictionary<string, ManagedMethodBody>.Empty;

    internal static FrozenDictionary<string, ManagedMethodBody> Index(
        IEnumerable<ManagedMethodBody> methods, IEnumerable<ManagedMethodBody> constructed)
    {
        var bodies = new Dictionary<string, ManagedMethodBody>(StringComparer.Ordinal);
        foreach (var body in methods.Concat(constructed)) bodies[body.Method.CanonicalName] = body;
        return bodies.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // A method's emission input: its analysed body, the structuring policy and its root map.
    // Fingerprinted once per root map: restoring the root map, outlining and restoring the
    // method all ask.
    internal UnitFingerprint? InputOf(string identity, MethodRootMap rootMap)
    {
        if (_inputs.TryGetValue(identity, out var known) && ReferenceEquals(known.RootMap, rootMap))
            return known.Input;
        var input = Fingerprint(identity, rootMap);
        _inputs[identity] = (rootMap, input);
        return input;
    }

    private UnitFingerprint? Fingerprint(string identity, MethodRootMap rootMap)
    {
        if (!Bodies.TryGetValue(identity, out var body)) return null;
        var input = new UnitFingerprintBuilder();
        try
        {
            input.Add(body);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        input.Add(Policy);
        input.Add(rootMap);
        var assemblies = new HashSet<string>(StringComparer.Ordinal);
        UnitAssemblies.Collect(body, assemblies);
        input.Add(assemblies.Count);
        foreach (var name in assemblies.Order(StringComparer.Ordinal))
        {
            input.Add(name);
            var shape = AssemblyShapes.GetValueOrDefault(name);
            input.Add((long)shape.First);
            input.Add((long)shape.Second);
        }
        return input.Finish();
    }

    internal WasmFunctionSymbols Symbols
    {
        get => _symbols;
        set
        {
            _symbols = value;
            _hasSpanLookup = value.BySymbol.TryGetAlternateLookup(out _bySymbol);
        }
    }

    // The units the last compile kept, opened before methods are structured.
    internal UnitStore Store { get; set; } = UnitStore.Empty;
    // What this compile keeps, once emission has published it; the pipeline commits it.
    internal UnitStoreChange? Change { get; set; }
    // Each method's function index in this compile's module, once its functions are bound.
    internal byte[]? FunctionIndices { get; set; }
    // The compile's instruction target, as the module emitter built it.
    internal InstructionModuleTarget? Target { get; set; }
    // The compile to keep once its result's facts are known (they are empty until then).
    internal UnitKeptCompile? Kept { get; set; }
    // The last compile to replay each emitted method against, when verification checks the
    // early cutoff's replay; set only when the cutoff applies.
    internal UnitReplay? Replay { get; set; }
    // Units restored in this compile; they stay where they are.
    internal ConcurrentBag<UnitLocation> Carried { get; } = [];
    // Units emitted in this compile.
    internal ConcurrentBag<CompiledUnit> Captured { get; } = [];
    // The program's identities by canonical name, built once if a query needs one.
    internal Lazy<UnitQueryArguments>? Program { get; set; }
    // Methods restored from their units in this compile.
    internal int Restored;
    // Methods emitted in this compile while units were on.
    internal int Emitted;
    // Methods not structured before planning.
    internal int Outlined;
    // Methods whose root maps were taken from their units.
    internal int RestoredRootMaps;
    // The facts each analysed method's root map was built from, by method identity.
    internal ConcurrentDictionary<string, ImmutableArray<UnitDependency>> RootMapDependencies { get; } =
        new(StringComparer.Ordinal);
    // The units methods' root maps were taken from, by method identity.
    internal ConcurrentDictionary<string, UnitLocation> RootMapSources { get; } = new(StringComparer.Ordinal);
    // Why units were not reused.
    internal int MissingUnits;
    internal int InputChanged;
    internal int UnresolvedSymbols;
    internal int UnbuildableQueries;
    internal int ProviderFailures;
    internal int ChangedAnswers;
    internal int CorruptUnits;
    internal ConcurrentDictionary<UnitQueryKind, int> UnbuildableKinds { get; } = new();
    // How many recorded queries of each kind answered differently, once per segment query.
    internal ConcurrentDictionary<UnitQueryKind, int> ChangedKinds { get; } = new();
    // A few unbuildable query keys, for the summary.
    internal ConcurrentDictionary<string, byte> UnbuildableKeys { get; } = new(StringComparer.Ordinal);

    public void Dispose()
    {
        Store.Dispose();
        Store = UnitStore.Empty;
    }

    // The function index a symbol names in this module, read without copying the symbol.
    internal bool TryResolve(ReadOnlySpan<char> symbol, out uint index) => _hasSpanLookup
        ? _bySymbol.TryGetValue(symbol, out index)
        : _symbols.BySymbol.TryGetValue(symbol.ToString(), out index);
}

internal interface ICompiledUnitSymbolBinder
{
    // Names the planned module's functions for the units captured from it.
    void Bind(WasmModulePlan plan, ModuleDataPlan data, System.Collections.Generic.IReadOnlyList<WasmFunctionImport> imports,
        System.Collections.Generic.IEnumerable<StructuredMethod> methods);
}

internal interface ICompiledUnitCapturer
{
    // Keeps a method's emission as a unit when it can be rebound and validated later.
    void Capture(string identity, ManagedBodyEmission emission, StructuredMethod method, MethodRootMap rootMap);
}

internal interface ICompiledUnitPublisher
{
    void Publish();
}

internal sealed class CompiledUnitSymbolBinder(
    CompiledUnitState state,
    IWasmFunctionSymbolBuilder symbols) : ICompiledUnitSymbolBinder
{
    public void Bind(WasmModulePlan plan, ModuleDataPlan data, System.Collections.Generic.IReadOnlyList<WasmFunctionImport> imports,
        System.Collections.Generic.IEnumerable<StructuredMethod> methods)
    {
        if (state.Directory is null)
            return;
        state.Symbols = symbols.Build(plan, data, imports);
        var functions = new List<KeyValuePair<string, int>>(state.Bodies.Count);
        foreach (var (identity, body) in state.Bodies)
        {
            if (plan.FunctionIndices.TryGetConstructedMethod(identity, out var index) ||
                plan.FunctionIndices.TryGetMethod(body.Method.Definition.Key, out index))
            {
                functions.Add(new(identity, index.Value));
            }
        }
        state.FunctionIndices = UnitFunctionIndices.Write(functions, imports.Count);
        state.Program = new(() => UnitQueryArguments.Of(state.Bodies.Values),
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    }
}

internal sealed class CompiledUnitCapturer(
    CompiledUnitState state,
    IWasmIndexOperandScanner scanner,
    IExceptionGroupEnumerator exceptionGroups) : ICompiledUnitCapturer
{
    // The facts a root map taken from a unit was built from; none if that unit cannot be read.
    private ImmutableArray<UnitDependency>? KeptRootMapDependencies(UnitLocation source)
    {
        try
        {
            return state.Store.Segments[source.Segment] is { } segment
                ? segment.Record(source.Offset).RootMapDependencies()
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public void Capture(string identity, ManagedBodyEmission emission, StructuredMethod method, MethodRootMap rootMap)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        ArgumentNullException.ThrowIfNull(emission);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(rootMap);
        System.Threading.Interlocked.Increment(ref emission.IsRestored ? ref state.Restored : ref state.Emitted);
        // A restored unit names its calls by symbol, so it serves the next compile where it is.
        if (emission.Unit is { } restored)
        {
            if (state.Directory is not null) state.Carried.Add(restored);
            return;
        }
        // A filter environment is shared with the method's filter funclets; such methods
        // are not kept until a unit can carry one.
        if (state.Directory is null || emission.Environment != FilterEnvironmentLayout.Empty &&
            (emission.Environment.Size != 0 || !emission.Environment.Captures.IsEmpty ||
             !emission.Environment.AnchorOffsets.IsEmpty || emission.Environment.RootSlotCount != 0))
        {
            return;
        }
        ImmutableArray<WasmIndexOperand> operands;
        try
        {
            operands = scanner.Scan(emission.Function.Body);
        }
        catch (InvalidDataException)
        {
            return;
        }
        var relocations = ImmutableArray.CreateBuilder<UnitRelocation>(operands.Length);
        foreach (var operand in operands)
        {
            string? symbol;
            if (operand.Space == WasmIndexSpace.Function)
            {
                if (!state.Symbols.ByIndex.TryGetValue(operand.Index, out symbol))
                    return;
            }
            else
            {
                // The other index spaces (memories, tags, types) are fixed by the module's shape.
                symbol = $"{operand.Space}:{operand.Index}";
            }
            relocations.Add(new(operand.Offset, operand.Length, operand.Space, symbol, operand.IsSigned33));
        }
        if (state.InputOf(identity, rootMap) is not { } input)
            return;
        var groups = state.Bodies.TryGetValue(identity, out var body)
            ? UnitExceptionGroups.Of(method, body.Body.ExceptionRegions, exceptionGroups)
            : null;
        // The root map is kept with the facts its analysis read: recorded in this compile, or
        // those of the unit it was taken from.
        ImmutableArray<UnitDependency>? rootMapDependencies =
            state.RootMapDependencies.TryGetValue(identity, out var recorded) ? recorded :
            state.RootMapSources.TryGetValue(identity, out var source) ? KeptRootMapDependencies(source) : null;
        state.Captured.Add(new(identity, input, emission.Dependencies,
            ImmutableArray.Create(emission.Function.Body), relocations.MoveToImmutable(),
            emission.Record.Emission.WasmInstructionCount)
        {
            FunctionName = emission.Function.Name,
            Parameters = emission.Function.Type.Parameters,
            Result = emission.Function.Type.Result,
            IsAggressiveInlining = emission.Function.IsAggressiveInlining,
            IsNoInlining = emission.Function.IsNoInlining,
            EnvironmentKey = emission.EnvironmentKey,
            BlockEmissionCounts = emission.Record.Emission.OriginalBlockEmissionCounts,
            ExceptionGroups = groups,
            RootMap = rootMapDependencies is null ? null : UnitRootMapFormat.Write(rootMap),
            RootMapDependencies = rootMapDependencies ?? [],
        });
    }
}

/// <summary>
/// Keeps this compile's units for the next: restored units stay in their segments, emitted
/// units go into one new segment, and the manifest is replaced. Segments that are mostly
/// dead, and all but the largest once there are too many, are merged into the new segment,
/// so the store's size follows the program and each compile writes what it changed. The
/// change is left on the state for the pipeline to commit through its storage.
/// </summary>
internal sealed class CompiledUnitPublisher(
    CompiledUnitState state) : ICompiledUnitPublisher
{
    private const int SegmentBudget = 16;

    public void Publish()
    {
        if (state.Directory is null)
            return;
        try
        {
            state.Change = Prepare(state.Store);
        }
        finally
        {
            // Restored bodies were copied out; the old segments can be released before files move.
            state.Dispose();
        }
    }

    private UnitStoreChange Prepare(UnitStore store)
    {
        var carried = new HashSet<UnitLocation>(state.Carried);
        if (state.Captured.IsEmpty && carried.Count == store.Count)
            return new(0, null, null, new HashSet<int>(), Summary(store.Count, store.Segments.Count));
        var emitted = state.Captured.OrderBy(unit => unit.Identity, StringComparer.Ordinal).ToArray();
        var emittedIdentities = new HashSet<string>(emitted.Select(unit => unit.Identity), StringComparer.Ordinal)
            .GetAlternateLookup<ReadOnlySpan<char>>();
        var segments = store.Segments;
        var live = new int[segments.Count];
        var kept = new List<UnitLocation>(carried.Count);
        foreach (var unit in carried)
        {
            if (segments[unit.Segment] is { } segment &&
                !emittedIdentities.Contains(segment.Record(unit.Offset).Identity))
            {
                kept.Add(unit);
                live[unit.Segment]++;
            }
        }
        kept.Sort(static (left, right) => left.Segment != right.Segment
            ? left.Segment.CompareTo(right.Segment)
            : left.Offset.CompareTo(right.Offset));
        var merge = new bool[segments.Count];
        var remaining = new List<int>();
        for (var segment = 0; segment < segments.Count; segment++)
        {
            if (live[segment] == 0) continue;
            if (live[segment] * 2 < store.UnitsAtWrite(segment)) merge[segment] = true;
            else remaining.Add(segment);
        }
        if (remaining.Count >= SegmentBudget)
        {
            var largest = remaining.MaxBy(segment => segments[segment]!.Length);
            foreach (var segment in remaining) merge[segment] = segment != largest;
        }
        var writer = new UnitSegmentWriter();
        foreach (var unit in emitted) writer.Add(unit);
        foreach (var unit in kept)
        {
            if (merge[unit.Segment]) writer.Add(segments[unit.Segment]!.Record(unit.Offset).ToUnit());
        }
        var manifestSegments = new List<UnitManifestSegment>();
        var ordinals = new int[segments.Count];
        Array.Fill(ordinals, -1);
        var entries = new List<UnitManifestEntry>(kept.Count + writer.Count);
        foreach (var unit in kept)
        {
            if (merge[unit.Segment]) continue;
            var segment = segments[unit.Segment]!;
            if (ordinals[unit.Segment] < 0)
            {
                ordinals[unit.Segment] = manifestSegments.Count;
                manifestSegments.Add(new(segment.Sequence, store.UnitsAtWrite(unit.Segment), segment.Length));
            }
            entries.Add(new(UnitStoreFormat.HashOf(segment.Record(unit.Offset).Identity),
                ordinals[unit.Segment], unit.Offset));
        }
        var sequence = store.NextSequence;
        if (writer.Count != 0)
        {
            var ordinal = manifestSegments.Count;
            manifestSegments.Add(new(sequence, writer.Count, writer.Length));
            foreach (var (hash, offset) in writer.Entries) entries.Add(new(hash, ordinal, offset));
        }
        return new(sequence, writer.Count != 0 ? writer : null,
            UnitManifestFormat.Write(manifestSegments, entries, sequence + 1),
            manifestSegments.Select(segment => segment.Sequence).ToHashSet(),
            Summary(entries.Count, manifestSegments.Count));
    }

    // A plain summary of the compile's reuse.
    private string Summary(int kept, int segments) =>
        $"restored {state.Restored} emitted {state.Emitted} kept {kept} in {segments} segments; " +
        $"outlined {state.Outlined}; root maps {state.RestoredRootMaps}; " +
        $"missing {state.MissingUnits} input {state.InputChanged} symbols {state.UnresolvedSymbols} " +
        $"unbuildable {state.UnbuildableQueries} failed {state.ProviderFailures} changed {state.ChangedAnswers} " +
        $"corrupt {state.CorruptUnits}\n" +
        (state.Replay is { } replay ? replay.Describe() + "\n" : "") +
        "unbuildable: " + string.Join(", ", state.UnbuildableKinds.OrderByDescending(pair => pair.Value)
            .Select(pair => $"{pair.Key} {pair.Value}")) + "\n" +
        "changed: " + string.Join(", ", state.ChangedKinds.OrderByDescending(pair => pair.Value)
            .Select(pair => $"{pair.Key} {pair.Value}")) + "\n" +
        string.Concat(state.UnbuildableKeys.Keys.Order(StringComparer.Ordinal).Select(key => key + "\n"));
}
