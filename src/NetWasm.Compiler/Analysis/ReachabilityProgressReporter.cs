// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Reports how far reachability has got, at most every Period: the methods and constructed types it has reached, the
// methods still queued, and the generic definitions with the most instances. A program whose analysis keeps growing
// grows there, through instances of a few generic methods and types. It does nothing unless a logger takes its
// entries, as the diagnostic log does.
internal sealed class ReachabilityProgressReporter(
    ILogger<ReachabilityProgressReporter> logger,
    TimeProvider timeProvider) : IReachabilityProgressObserver
{
    internal static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    internal const int Listed = 5;

    private static readonly Action<ILogger, int, int, int, Exception?> LogCounts =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(4004, "ReachabilityProgress"),
            "Reachability: {Methods:N0} methods, {Types:N0} constructed types, {Queued:N0} queued");

    private static readonly Action<ILogger, string, string, Exception?> LogMost =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4005, "ReachabilityGenericGrowth"),
            "Most {Kind}: {Definitions}");

    private readonly ILogger<ReachabilityProgressReporter> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ??
        throw new ArgumentNullException(nameof(timeProvider));
    // When it last reported, or when it was first told of progress; zero before that. Compilations a host runs side
    // by side share it, and at worst one report is skipped or repeated.
    private long _reportedAt;

    public void Observe(ReachabilityLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (!_logger.IsEnabled(LogLevel.Information)) return;
        var now = _timeProvider.GetTimestamp();
        var reportedAt = Volatile.Read(ref _reportedAt);
        if (reportedAt == 0)
        {
            Volatile.Write(ref _reportedAt, now);
            return;
        }
        if (_timeProvider.GetElapsedTime(reportedAt, now) < Period) return;
        Volatile.Write(ref _reportedAt, now);
        LogCounts(_logger, ledger.MethodInstances.Count, ledger.ConstructedTypes.Count, ledger.Pending.Count, null);
        var methods = Most(ledger.MethodInstances.Values
            .Where(static method => !method.MethodArguments.IsDefaultOrEmpty ||
                !method.DeclaringType.TypeArguments.IsDefaultOrEmpty)
            .Select(static method => $"{Name(method.DeclaringType)}.{method.Definition.Name}"));
        if (methods.Length != 0) LogMost(_logger, "generic method instances", methods, null);
        var types = Most(ledger.ConstructedTypes
            .Where(static type => !type.TypeArguments.IsDefaultOrEmpty)
            .Select(Name));
        if (types.Length != 0) LogMost(_logger, "generic type instances", types, null);
    }

    // A type's name without its type arguments, so that its instances count together: an instantiation is named by
    // its generic definition.
    private static string Name(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation
            ? Name(type.ElementType!)
            : type.FullName ?? type.CanonicalName;

    private static string Most(IEnumerable<string> definitions) => string.Join(", ", definitions
        .GroupBy(static name => name, StringComparer.Ordinal)
        .Select(static group => (Name: group.Key, Count: group.Count()))
        .OrderByDescending(static entry => entry.Count)
        .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
        .Take(Listed)
        .Select(static entry => $"{entry.Name} ({entry.Count:N0})"));
}
