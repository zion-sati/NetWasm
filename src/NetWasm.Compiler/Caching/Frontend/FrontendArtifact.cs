using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed record FrontendArtifact(
    ReachableMethodAnalysis Analysis,
    StructuredMethod StructuredMethod);
