// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class LoggingReachabilityClosureObserverTests
{
    [Fact]
    public void ObserveWritesOneAggregateDiagnostic()
    {
        var logger = new CompilerTestLogger<LoggingReachabilityClosureObserver>();
        var observer = new LoggingReachabilityClosureObserver(logger);

        observer.Observe(new ReachabilityClosureObservation(
            3,
            TimeSpan.FromMilliseconds(4),
            5,
            2,
            TimeSpan.FromMilliseconds(6),
            TimeSpan.FromMilliseconds(7)));

        Assert.Equal(1, logger.EntryCount);
        Assert.Equal(LogLevel.Debug, logger.Level);
        Assert.Contains("methodRequests=3", logger.Message, StringComparison.Ordinal);
        Assert.Contains("dispatches=5", logger.Message, StringComparison.Ordinal);
        Assert.Contains("resolvedDispatches=2", logger.Message, StringComparison.Ordinal);
    }
}
