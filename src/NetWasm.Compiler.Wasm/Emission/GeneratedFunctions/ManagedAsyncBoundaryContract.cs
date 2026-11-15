// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
}
