// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow;
using New = NetWasm.Compiler.ControlFlow.Structured;
using StructuredMethod = NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerComplexityMetricsCollectorTests
{
    [Fact]
    public void RecordsComparableStructuralAndEmissionMetricsForEveryManagedMethod()
    {
        using var context = CompilerInvariantTestContext.Create();
        var symbols = MetadataCompilationTestActors.Symbols(context.Metadata.Snapshot);
        var lowering = ValidationTestData.Lower(context.Result.Program);
        var emissions = lowering.Methods
            .Select(pair => new WasmManagedMethodEmissionMetric(
                pair.Key.ToString(),
                17,
                41,
                23,
                65_536,
                pair.Value.Blocks.Keys
                    .ToImmutableDictionary(block => block.Value, _ => 1)))
            .Concat(lowering.ConstructedMethods.Select(pair =>
                new WasmManagedMethodEmissionMetric(
                    pair.Key,
                    17,
                    41,
                    23,
                    65_536,
                    pair.Value.Blocks.Keys
                        .ToImmutableDictionary(block => block.Value, _ => 1))))
            .ToImmutableArray();

        var report = CreateCollector().Collect(
            symbols,
            context.Result.Program,
            lowering,
            emissions);

        Assert.Equal(emissions.Length, report.Methods.Length);
        Assert.All(report.Methods, metric =>
        {
            Assert.Contains("::", metric.Identity, StringComparison.Ordinal);
            Assert.True(metric.CilInstructionCount > 0);
            Assert.True(metric.OriginalBlockCount > 0);
            Assert.Equal(metric.ReachableOriginalBlockCount,
                metric.OriginalBodyEmissionCount);
            Assert.Empty(metric.MissingOriginalBlocks);
            Assert.Empty(metric.DuplicatedOriginalBlocks);
            Assert.Equal(17, metric.WasmInstructionCount);
            Assert.Equal(41, metric.WasmBodyBytes);
            Assert.Equal(23, metric.CompileDurationTicks);
            Assert.Equal(65_536, metric.PeakObservedManagedMemoryBytes);
        });
        Assert.Equal(
            report.Methods.Sum(metric => metric.CilInstructionCount),
            report.TotalCilInstructions);
        Assert.Equal(
            report.Methods.Sum(metric => metric.WasmBodyBytes),
            report.TotalWasmBodyBytes);
    }

    [Fact]
    public void RejectsMissingOrUninitializedEmissionMeasurements()
    {
        using var context = CompilerInvariantTestContext.Create();
        var collector = CreateCollector();
        var symbols = MetadataCompilationTestActors.Symbols(context.Metadata.Snapshot);
        var lowering = ValidationTestData.Lower(context.Result.Program);
        Assert.Throws<ArgumentException>(() => collector.Collect(
            symbols,
            context.Result.Program,
            lowering,
            default));
        Assert.Throws<InvalidOperationException>(() => collector.Collect(
            symbols,
            context.Result.Program,
            lowering,
            []));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            null!,
            context.Result.Program,
            lowering,
            []));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            symbols,
            null!,
            lowering,
            []));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            symbols,
            context.Result.Program,
            null!,
            []));
    }

    [Fact]
    public void CountsEveryStructuredRegionAndExceptionPart()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = Assert.Single(original.Blocks.Values);
        var owner = new New.StructuredBlockOccurrence(
            block.Id,
            New.StructuredBlockRole.Owner);
        var replica = owner with { Role = New.StructuredBlockRole.ExecutingReplica };
        var leaf = new New.StructuredSequence([new New.StructuredCode(owner)]);
        var clause = new New.StructuredExceptionClause(
            CilExceptionRegionKind.Catch,
            0,
            1,
            new EntityKey(ValidationTestData.Assembly, 0x01000001),
            null,
            leaf,
            null)
        {
            HandlerBlock = block.Id,
        };
        var nested = new New.StructuredExceptionGroup(
            new(1),
            new(0),
            0,
            1,
            [new New.StructuredExceptionCode(leaf)],
            [],
            [],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var group = new New.StructuredExceptionGroup(
            new(0),
            null,
            0,
            1,
            [new New.StructuredExceptionCode(leaf), new New.StructuredNestedExceptionGroup(nested.Id)],
            [clause],
            [new New.StructuredExceptionContinuation(new(0), 0, block.Id, leaf)],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var body = new New.StructuredSequence(
        [
            new New.StructuredCode(replica),
            new New.StructuredIf(replica, leaf, leaf),
            new New.StructuredLoop(replica, true, leaf, leaf, leaf),
            new New.StructuredPostTestLoop(leaf, replica, false, leaf, leaf),
            new New.StructuredExceptionRegion(group.Id, new(0)),
            new New.StructuredExceptionRegion(group.Id, null),
            new New.StructuredExceptionRegion(group.Id, null),
            new New.StructuredDispatcher(
                block.Id,
                [new New.StructuredDispatcherBlock(replica, null, null)],
                [new New.StructuredDispatcherExit(block.Id, leaf)]),
            new New.StructuredLoopBreak(),
            new New.StructuredLoopContinue(),
            new New.StructuredDispatcherContinue(block.Id),
        ]);
        var method = original with
        {
            Body = body,
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId,
                New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group)
                .Add(nested.Id, nested),
        };
        var program = ValidationTestData.Program();
        var lowering = new WasmMethodLoweringResult(
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty.Add(
                method.Header.Method.Key, method),
            []);
        var identity = method.Header.Method.Key.ToString();
        var emission = new WasmManagedMethodEmissionMetric(
            identity,
            1,
            1,
            1,
            1,
            method.Blocks.Keys.ToImmutableDictionary(block => block.Value, _ => 1));

        var logger = new CompilerTestLogger<CompilerComplexityMetricsCollector>();
        var report = new CompilerComplexityMetricsCollector(
            logger,
            new StructuredBlockOwnershipAnalyzer()).Collect(
            ValidationTestData.Symbols(),
            program,
            lowering,
            [emission]);

        Assert.True(report.Methods.Single().SyntheticBlockCount > 0);
        Assert.True(logger.EntryCount > 0);

        var duplicated = emission with
        {
            OriginalBlockEmissionCounts = method.Blocks.Keys
                .ToImmutableDictionary(block => block.Value, _ => 2),
        };
        var duplicatedReport = CreateCollector().Collect(
            ValidationTestData.Symbols(),
            program,
            lowering,
            [duplicated]);
        Assert.NotEmpty(duplicatedReport.Methods.Single().DuplicatedOriginalBlocks);
    }

    private static CompilerComplexityMetricsCollector CreateCollector() => new(
        NullLogger<CompilerComplexityMetricsCollector>.Instance,
        new StructuredBlockOwnershipAnalyzer());

}
