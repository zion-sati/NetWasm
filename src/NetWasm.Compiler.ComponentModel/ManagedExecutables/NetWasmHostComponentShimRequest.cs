using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed record NetWasmHostComponentShimRequest(
    string OutputPath,
    ComponentTarget Target,
    ManagedExecutableCompletionShape CompletionShape = ManagedExecutableCompletionShape.Synchronous,
    bool StructuredDiagnostics = false);
