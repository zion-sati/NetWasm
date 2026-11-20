// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

// Warns for each reached method whose IL names a member its declaring type lacks: the method compiles to a body that
// throws MissingMethodException or MissingFieldException when called, as Native AOT compiles it.
internal sealed class MissingMemberReporter(ILogger<MissingMemberReporter> logger) : IMissingMemberReporter
{
    private static readonly Action<ILogger, string, string, string, Exception?> LogMissing =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId((int)DiagnosticCode.MissingApi, "NW1016"),
            "{Method} references '{Member}', which NetWasm does not have; calling it throws {Exception}. Use an API " +
            "NetWasm has, keep the call unreached, or set NetWasmMissingApiAsError to make this an error.");

    public void Report(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!logger.IsEnabled(LogLevel.Warning))
            return;
        var missing = program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .Select(static body => body.Body.MissingMember)
            .OfType<MissingMemberReference>()
            .DistinctBy(static member => (member.Method, member.Member))
            .OrderBy(static member => member.Method, StringComparer.Ordinal)
            .ThenBy(static member => member.Member, StringComparer.Ordinal);
        foreach (var member in missing)
        {
            LogMissing(logger, member.Method, member.Member, member.ExceptionType["System.".Length..], null);
        }
    }
}
