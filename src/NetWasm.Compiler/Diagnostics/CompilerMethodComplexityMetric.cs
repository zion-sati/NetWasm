using System.Collections.Immutable;

namespace NetWasm.Compiler.Diagnostics;

public sealed record CompilerMethodComplexityMetric(
    string Identity,
    int CilInstructionCount,
    int OriginalBlockCount,
    int ReachableOriginalBlockCount,
    int CfgEdgeCount,
    int ExceptionRegionCount,
    int MaximumEntryStackDepth,
    int IrBlockCount,
    int SyntheticBlockCount,
    int OriginalBodyEmissionCount,
    ImmutableArray<int> MissingOriginalBlocks,
    ImmutableArray<int> DuplicatedOriginalBlocks,
    int WasmInstructionCount,
    int WasmBodyBytes,
    long CompileDurationTicks,
    long? PeakObservedManagedMemoryBytes);
