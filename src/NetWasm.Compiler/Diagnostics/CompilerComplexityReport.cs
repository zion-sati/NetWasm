using System.Collections.Immutable;

namespace NetWasm.Compiler.Diagnostics;

public sealed record CompilerComplexityReport(
    ImmutableArray<CompilerMethodComplexityMetric> Methods,
    int TotalCilInstructions,
    int TotalOriginalBlocks,
    int TotalCfgEdges,
    int TotalWasmInstructions,
    int TotalWasmBodyBytes);
