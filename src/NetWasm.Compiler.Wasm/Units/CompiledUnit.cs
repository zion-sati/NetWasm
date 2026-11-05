using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>A module index in a unit's body, named by the symbol it is rebound by.</summary>
internal readonly record struct UnitRelocation(
    int Offset,
    int Length,
    WasmIndexSpace Space,
    string Symbol,
    bool IsSigned33);

/// <summary>
/// One method's emitted body, as a managed value (a unit emitted in this compile, or one
/// read out of a segment for compaction), with what it was built from: the fingerprint of its own
/// emission input, the queries its emission made with their answers, and the module
/// indices it encodes by symbol. It is reused while its input and every answer are
/// unchanged; its indices are rebound to the new module's.
/// </summary>
internal sealed record CompiledUnit(
    string Identity,
    UnitFingerprint Input,
    ImmutableArray<UnitDependency> Dependencies,
    ImmutableArray<byte> Body,
    ImmutableArray<UnitRelocation> Relocations,
    int InstructionCount)
{
    // The function the body defines, as emission named and typed it.
    public string FunctionName { get; init; } = "";
    public ImmutableArray<NetWasm.Compiler.Core.CliValueKind> Parameters { get; init; } = [];
    public NetWasm.Compiler.Core.CliValueKind Result { get; init; }
    public bool IsAggressiveInlining { get; init; }
    public bool IsNoInlining { get; init; }
    public string EnvironmentKey { get; init; } = "";
    // How many times emission wrote each of the method's original blocks.
    public ImmutableDictionary<int, int> BlockEmissionCounts { get; init; } =
        ImmutableDictionary<int, int>.Empty;
    // The method's exception groups as structuring formed them, in planning's order; null
    // when the method cannot stand in for its structured form.
    public ImmutableArray<UnitExceptionGroup>? ExceptionGroups { get; init; }
    // The method's root map and the whole-program facts its analysis read; null when the
    // root map is not kept.
    public ImmutableArray<byte>? RootMap { get; init; }
    public ImmutableArray<UnitDependency> RootMapDependencies { get; init; } = [];
}

/// <summary>An exception group as structuring formed it: its clauses are body exception regions.</summary>
internal readonly record struct UnitExceptionGroup(int Id, int Parent, int TryOffset, int TryLength, ImmutableArray<int> Regions);
