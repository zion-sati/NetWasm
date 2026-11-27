// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Analysis.ReceiverFlow;

namespace NetWasm.Compiler.Diagnostics;

// Writes the receiver flow report among the diagnostic artifacts, or beside the diagnostic log when the compile writes
// no artifacts (a trace-level log), so a log sent back with a build carries it.
internal sealed class CompilerReceiverFlowReportWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticLogPathResolver logPaths,
    ICompilerDiagnosticJsonArtifactWriter json) : ICompilerReceiverFlowReportWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticLogPathResolver _logPaths =
        logPaths ?? throw new ArgumentNullException(nameof(logPaths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));

    public void Write(CompilerOptions options, ReceiverFlowReport? report)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (report is null) return;
        var path = _paths.ResolvePath(options, "03-receiver-flow.json") ??
            (_logPaths.Resolve(options) is { } log ? log + ".receiver-flow.json" : null);
        if (path is not null) _json.WriteJson(path, report, CompilerDiagnosticArtifactJsonContext.Default.ReceiverFlowReport);
    }
}
