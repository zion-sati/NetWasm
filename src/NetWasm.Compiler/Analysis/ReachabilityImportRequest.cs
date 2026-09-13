using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record ReachabilityImportRequest(
    MethodInstanceModel Method,
    bool IsOutwardBoundary = false);
