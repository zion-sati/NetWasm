using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record ManagedAsyncBoundaryNames(
    string Status,
    string? Result,
    string Complete)
{
    public static ManagedAsyncBoundaryNames ForExport(
        JavaScriptAsyncMethodBinding binding) => new(
        JavaScriptAsyncAbiNames.ExportStatus(binding.Method),
        binding.Return.HasResult
            ? JavaScriptAsyncAbiNames.ExportResult(binding.Method)
            : null,
        JavaScriptAsyncAbiNames.ExportComplete(binding.Method));

    public static ManagedAsyncBoundaryNames ForProcess(
        JavaScriptAsyncMethodBinding binding) => new(
        "netwasm.process.status",
        binding.Return.HasResult ? "netwasm.process.result" : null,
        "netwasm.process.complete");
}

internal sealed record ManagedAsyncBoundaryKinds(
    ManagedBoundaryKind Start,
    ManagedBoundaryKind Observation,
    ManagedBoundaryKind Completion)
{
    public static ManagedAsyncBoundaryKinds Export { get; } = new(
        ManagedBoundaryKind.AsynchronousExportStart,
        ManagedBoundaryKind.AsynchronousExportObservation,
        ManagedBoundaryKind.AsynchronousExportCompletion);

    public static ManagedAsyncBoundaryKinds Process { get; } = new(
        ManagedBoundaryKind.AsynchronousProcessStart,
        ManagedBoundaryKind.AsynchronousProcessObservation,
        ManagedBoundaryKind.AsynchronousProcessCompletion);
}
