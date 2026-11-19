// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using System.Collections.Generic;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ReachabilityProgressReporterTests
{
    private static readonly AssemblyIdentity Assembly = new("Fixtures");
    private static readonly CliTypeIdentity Box = CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Fixtures", "Pair`2", false);
    private static readonly CliTypeIdentity Plain = CliTypeIdentity.Named(Assembly, "Fixtures", "Plain", false);
    private static readonly CliTypeIdentity Other = CliTypeIdentity.Named(Assembly, "Fixtures", "Other", false);

    [Fact]
    public void ReportsNothingUntilAPeriodHasPassedSinceItFirstHeardOfProgress()
    {
        var (reporter, logger, time) = Create();
        var ledger = Ledger();

        reporter.Observe(ledger);
        time.Advance(ReachabilityProgressReporter.Period - TimeSpan.FromTicks(1));
        reporter.Observe(ledger);

        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void ReportsCountsAndTheGenericDefinitionsWithTheMostInstancesOncePerPeriod()
    {
        var (reporter, logger, time) = Create();
        var ledger = Ledger();
        reporter.Observe(ledger);
        time.Advance(ReachabilityProgressReporter.Period);

        reporter.Observe(ledger);
        reporter.Observe(ledger);

        Assert.Equal(
        [
            "Reachability: 5 methods, 4 constructed types, 1 queued",
            "Most generic method instances: Fixtures.Box`1.Get (2), Fixtures.Plain.Map (2)",
            "Most generic type instances: Fixtures.Box`1 (2), Fixtures.Pair`2 (1)",
        ], logger.Messages);
        Assert.All(logger.Levels, level => Assert.Equal(LogLevel.Information, level));

        time.Advance(ReachabilityProgressReporter.Period);
        reporter.Observe(ledger);
        Assert.Equal(6, logger.Messages.Count);
    }

    [Fact]
    public void ListsAtMostTheFirstDefinitionsByCountThenName()
    {
        var (reporter, logger, time) = Create();
        var ledger = new ReachabilityLedger();
        for (var index = 0; index < ReachabilityProgressReporter.Listed + 2; index++)
        {
            var definition = CliTypeIdentity.Named(Assembly, "Fixtures", $"G{index}`1", false);
            for (var copy = 0; copy <= index % 2; copy++)
                ledger.ConstructedTypes.Add(CliTypeIdentity.GenericInstantiation(definition, [Copy(copy)]));
        }
        reporter.Observe(ledger);
        time.Advance(ReachabilityProgressReporter.Period);

        reporter.Observe(ledger);

        Assert.Equal(
        [
            "Reachability: 0 methods, 10 constructed types, 0 queued",
            "Most generic type instances: Fixtures.G1`1 (2), Fixtures.G3`1 (2), Fixtures.G5`1 (2), " +
                "Fixtures.G0`1 (1), Fixtures.G2`1 (1)",
        ], logger.Messages);
    }

    [Fact]
    public void DoesNothingWhenNoLoggerTakesItsEntries()
    {
        var (reporter, logger, time) = Create();
        logger.Enabled = false;
        var ledger = Ledger();

        reporter.Observe(ledger);
        time.Advance(ReachabilityProgressReporter.Period);
        reporter.Observe(ledger);

        Assert.Empty(logger.Messages);
        Assert.Throws<ArgumentNullException>(() => reporter.Observe(null!));
    }

    [Fact]
    public void RequiresItsCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ReachabilityProgressReporter(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new ReachabilityProgressReporter(new RecordingLogger(), null!));
    }

    private static (ReachabilityProgressReporter Reporter, RecordingLogger Logger, ManualTimeProvider Time) Create()
    {
        var logger = new RecordingLogger();
        var time = new ManualTimeProvider();
        return (new ReachabilityProgressReporter(logger, time), logger, time);
    }

    // Five methods: two instances of Box<T>.Get, two of the generic method Plain.Map<U>, and the plain Plain.Run;
    // four constructed types: Box<Plain>, Box<Other>, Pair<Plain, Other> and Plain; one method still queued.
    private static ReachabilityLedger Ledger()
    {
        var ledger = new ReachabilityLedger();
        var boxOfPlain = CliTypeIdentity.GenericInstantiation(Box, [Plain]);
        var boxOfOther = CliTypeIdentity.GenericInstantiation(Box, [Other]);
        Add(ledger, Method(boxOfPlain, "Get", []));
        Add(ledger, Method(boxOfOther, "Get", []));
        Add(ledger, Method(Plain, "Map", [Plain]));
        Add(ledger, Method(Plain, "Map", [Other]));
        Add(ledger, Method(Plain, "Run", []));
        ledger.ConstructedTypes.Add(boxOfPlain);
        ledger.ConstructedTypes.Add(boxOfOther);
        ledger.ConstructedTypes.Add(CliTypeIdentity.GenericInstantiation(Pair, [Plain, Other]));
        ledger.ConstructedTypes.Add(Plain);
        ledger.Pending.Enqueue(Method(Plain, "Later", []));
        return ledger;
    }

    private static CliTypeIdentity Copy(int copy) => copy == 0 ? Plain : Other;

    private static void Add(ReachabilityLedger ledger, MethodInstanceModel method) =>
        ledger.MethodInstances.Add(method.CanonicalName, method);

    private static MethodInstanceModel Method(
        CliTypeIdentity declaringType, string name, System.Collections.Immutable.ImmutableArray<CliTypeIdentity> arguments)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000001 + name.Length), new EntityKey(Assembly, 0x02000002), name, true,
            signature, 1)
        {
            GenericArity = arguments.Length,
        };
        return new MethodInstanceModel(definition, declaringType, arguments, signature);
    }

    private sealed class RecordingLogger : ILogger<ReachabilityProgressReporter>
    {
        public bool Enabled { get; set; } = true;

        public List<string> Messages { get; } = [];

        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => Enabled;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Levels.Add(logLevel);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
