// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

// Warns for each attribute query the settled program's reached code leaves to its run-time path, which throws
// PlatformNotSupportedException when reached, so the build says so first: the method whose code holds it, the
// reference, why the compile did not answer it and what would.
internal sealed class UnplannedAttributeQueryReporter(ILogger<UnplannedAttributeQueryReporter> logger)
    : IUnplannedAttributeQueryReporter
{
    private static readonly Action<ILogger, string, string, string, string, string, Exception?> LogUnplanned =
        LoggerMessage.Define<string, string, string, string, string>(
            LogLevel.Warning,
            new EventId((int)DiagnosticCode.UnplannedAttributeQuery, "NW1019"),
            "{Method} {Query}, which NetWasm cannot answer at compile time ({Reason}); {Outcome}. {Remedy}");

    public void Report(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!logger.IsEnabled(LogLevel.Warning))
            return;
        var unplanned = program.UnplannedAttributeQueries
            .Distinct()
            .OrderBy(static query => query.Method, StringComparer.Ordinal)
            .ThenBy(static query => query.IlOffset)
            .ThenBy(static query => query.Query, StringComparer.Ordinal);
        foreach (var query in unplanned)
        {
            LogUnplanned(logger, query.Method, query.Query, query.Reason, query.Outcome, query.Remedy, null);
        }
    }
}
