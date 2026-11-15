// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests.Analysis.Delegates;

public sealed class DiagnosticDelegateBindingPlannerTests
{
    [Fact]
    public void DecoratorDelegatesAndLogsTheBindingCount()
    {
        var expected = ImmutableArray.Create<ManagedDelegateBinding>();
        var inner = new RecordingPlanner(expected);
        var logger = new RecordingLogger();
        var planner = new[]
        {
            new DiagnosticDelegateBindingPlanner(inner, logger),
        }.Cast<IDelegateBindingPlanner>().Single();

        var actual = planner.Plan([], []);

        Assert.Equal(expected, actual);
        Assert.Equal(1, inner.CallCount);
        Assert.Equal(4300, Assert.Single(logger.Events).Id);
        Assert.Contains("0", logger.Events[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecoratorAndFactoryRejectMissingDependencies()
    {
        var planner = new RecordingPlanner([]);
        var logger = NullLogger<DiagnosticDelegateBindingPlanner>.Instance;
        var identities = new ManagedMethodIdentityFactory();

        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticDelegateBindingPlanner(null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticDelegateBindingPlanner(planner, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateBindingPlannerFactory(null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateBindingPlannerFactory(identities, null!));
    }

    [Fact]
    public void FactoryCreatesTheDecoratedPlannerThroughItsContract()
    {
        var factory = new[]
        {
            new DelegateBindingPlannerFactory(
                new ManagedMethodIdentityFactory(),
                NullLogger<DiagnosticDelegateBindingPlanner>.Instance),
        }.Cast<IDelegateBindingPlannerFactory>().Single();

        var planner = factory.Create(new RejectingRelationships());

        Assert.IsType<DiagnosticDelegateBindingPlanner>(planner);
        Assert.Empty(planner.Plan([], []));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }

    private sealed class RecordingPlanner(
        ImmutableArray<ManagedDelegateBinding> result) : IDelegateBindingPlanner
    {
        public int CallCount { get; private set; }

        public ImmutableArray<ManagedDelegateBinding> Plan(
            IEnumerable<MethodInstanceModel> invokes,
            IEnumerable<MethodInstanceModel> targets)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class RejectingRelationships : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) => new(TypeRelationshipCharacteristics.None);
    }

    private sealed class RecordingLogger :
        ILogger<DiagnosticDelegateBindingPlanner>
    {
        public List<(EventId Id, string Message)> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Events.Add((eventId, formatter(state, exception)));
    }
}
