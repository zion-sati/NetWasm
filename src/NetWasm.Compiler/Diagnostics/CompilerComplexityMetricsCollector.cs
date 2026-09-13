using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using Structured = NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

using Microsoft.Extensions.Logging;
namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerComplexityMetricsCollector(
    ILogger<CompilerComplexityMetricsCollector> logger,
    IStructuredBlockOwnershipAnalyzer blockOwnershipAnalyzer) :
    ICompilerComplexityMetricsCollector
{
    private readonly ILogger<CompilerComplexityMetricsCollector> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IStructuredBlockOwnershipAnalyzer _blockOwnershipAnalyzer =
        blockOwnershipAnalyzer ??
        throw new ArgumentNullException(nameof(blockOwnershipAnalyzer));


    private static readonly Action<ILogger, string, int, string, Exception?> LogBlockOwnershipVisit =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Trace,
            new EventId(4102, nameof(LogBlockOwnershipVisit)),
            "Structured block ownership {EmissionIdentity} block {BlockIndex} at {Path}.");

    private static readonly Action<ILogger, string, int, string, string, Exception?> LogDuplicateBlockOwnership =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Warning,
            new EventId(4103, nameof(LogDuplicateBlockOwnership)),
            "Structured block ownership conflict {EmissionIdentity} block {BlockIndex}: first {FirstPath}, duplicate {DuplicatePath}.");
    public CompilerComplexityReport Collect(
        ISymbolFormatter symbols,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        ImmutableArray<WasmManagedMethodEmissionMetric> emissions)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(lowering);
        if (emissions.IsDefault)
        {
            throw new ArgumentException(
                "managed method emission metrics must be initialized",
                nameof(emissions));
        }

        var emissionsByIdentity = emissions.ToImmutableDictionary(
            emission => emission.Identity,
            StringComparer.Ordinal);
        var methods = program.Methods
            .Select(pair => Analyze(
                pair.Key.ToString(),
                symbols.Format(pair.Value.Method.Definition),
                pair.Value,
                lowering.Methods[pair.Key],
                emissionsByIdentity,
            _blockOwnershipAnalyzer,
            _logger))
            .Concat(program.ConstructedMethods.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)
                .Select(pair => Analyze(
                    pair.Key,
                    pair.Key,
                    pair.Value,
                    lowering.ConstructedMethods[pair.Key],
                    emissionsByIdentity,
            _blockOwnershipAnalyzer,
            _logger)))
            .OrderBy(metric => metric.Identity, StringComparer.Ordinal)
            .ToImmutableArray();
        return new(
            methods,
            methods.Sum(metric => metric.CilInstructionCount),
            methods.Sum(metric => metric.OriginalBlockCount),
            methods.Sum(metric => metric.CfgEdgeCount),
            methods.Sum(metric => metric.WasmInstructionCount),
            methods.Sum(metric => metric.WasmBodyBytes));
    }

    private static CompilerMethodComplexityMetric Analyze(
        string emissionIdentity,
        string displayIdentity,
        ManagedMethodBody method,
        Structured.StructuredMethod structured,
        ImmutableDictionary<string, WasmManagedMethodEmissionMetric> emissions,
        IStructuredBlockOwnershipAnalyzer blockOwnershipAnalyzer,
        ILogger<CompilerComplexityMetricsCollector> diagnosticLogger)
    {
        if (!emissions.TryGetValue(emissionIdentity, out var emission))
        {
            throw new InvalidOperationException(
                $"managed method emission metrics are missing for '{emissionIdentity}'");
        }

        if (diagnosticLogger.IsEnabled(LogLevel.Trace) ||
            diagnosticLogger.IsEnabled(LogLevel.Warning))
        {
            foreach (var visit in blockOwnershipAnalyzer.Analyze(structured))
            {
                if (visit.FirstPath is null)
                {
                    LogBlockOwnershipVisit(
                        diagnosticLogger,
                        displayIdentity,
                        visit.BlockIndex,
                        visit.Path,
                        null);
                }
                else
                {
                    LogDuplicateBlockOwnership(
                        diagnosticLogger,
                        displayIdentity,
                        visit.BlockIndex,
                        visit.FirstPath,
                        visit.Path,
                        null);
                }
            }
        }

        var inventory = StructuredBlockInventory.Create(structured);
        var missing = structured.Blocks.Keys
            .Select(block => block.Value)
            .Where(index => !emission.OriginalBlockEmissionCounts.ContainsKey(index))
            .Order()
            .ToImmutableArray();
        var duplicated = emission.OriginalBlockEmissionCounts
            .Where(pair => pair.Value != 1)
            .Select(pair => pair.Key)
            .Order()
            .ToImmutableArray();

        var graph = method.ControlFlow.Graph;
        return new(
            displayIdentity,
            method.Body.Instructions.Length,
            graph.Blocks.Length,
            graph.ReachableBlocks.Count,
            graph.Successors.Values.Sum(successors => successors.Length),
            method.Body.ExceptionRegions.Length,
            method.ControlFlow.EntryStacks.Values
                .Select(stack => stack.Length)
                .DefaultIfEmpty()
                .Max(),
            structured.Blocks.Count + inventory.SyntheticBlockCount,
            inventory.SyntheticBlockCount,
            emission.OriginalBlockEmissionCounts.Values.Sum(),
            missing,
            duplicated,
            emission.WasmInstructionCount,
            emission.WasmBodyBytes,
            emission.CompileDurationTicks,
            emission.PeakObservedManagedMemoryBytes);
    }

    private sealed record StructuredBlockInventory(int SyntheticBlockCount)
    {
        public static StructuredBlockInventory Create(Structured.StructuredMethod method)
        {
            var groups = new HashSet<Structured.StructuredExceptionGroupId>();
            var synthetic = 0;
            VisitSequence(method.Body);
            return new(synthetic);

            void VisitSequence(Structured.StructuredSequence sequence)
            {
                foreach (var region in sequence.Regions)
                {
                    switch (region)
                    {
                        case Structured.StructuredCode code:
                            if (code.Occurrence.Role != Structured.StructuredBlockRole.Owner)
                                synthetic++;
                            break;
                        case Structured.StructuredIf branch:
                            synthetic++;
                            VisitSequence(branch.WhenTrue);
                            VisitSequence(branch.WhenFalse);
                            break;
                        case Structured.StructuredLoop loop:
                            synthetic++;
                            VisitSequence(loop.Body);
                            VisitSequence(loop.ContinueBody);
                            VisitSequence(loop.ExitBody);
                            break;
                        case Structured.StructuredPostTestLoop loop:
                            synthetic++;
                            VisitSequence(loop.Body);
                            VisitSequence(loop.ContinueBody);
                            VisitSequence(loop.ExitBody);
                            break;
                        case Structured.StructuredExceptionRegion exception:
                            synthetic++;
                            VisitGroup(
                                exception.Group,
                                exception.FallthroughContinuation);
                            break;
                        case Structured.StructuredDispatcher dispatcher:
                            synthetic += 1 + dispatcher.Exits.Length +
                                dispatcher.Blocks.Count(block =>
                                    block.Occurrence.Role != Structured.StructuredBlockRole.Owner);
                            foreach (var exit in dispatcher.Exits)
                            {
                                VisitSequence(exit.Body);
                            }
                            break;
                        case Structured.StructuredLoopBreak or Structured.StructuredLoopContinue:
                            synthetic++;
                            break;
                    }
                }
            }

            void VisitGroup(
                Structured.StructuredExceptionGroupId groupId,
                Structured.StructuredContinuationId? fallthrough)
            {
                if (!groups.Add(groupId))
                {
                    return;
                }
                var group = method.ExceptionGroups[groupId];
                synthetic++;
                foreach (var part in group.ProtectedParts)
                {
                    if (part is Structured.StructuredExceptionCode code)
                    {
                        VisitSequence(code.Body);
                    }
                    else if (part is Structured.StructuredNestedExceptionGroup nested)
                    {
                        VisitGroup(nested.Group, null);
                    }
                }
                foreach (var clause in group.Clauses)
                {
                    if (clause.FilterBody is not null)
                    {
                        VisitSequence(clause.FilterBody);
                    }
                    VisitSequence(clause.HandlerBody);
                }
                for (var index = 0; index < group.NormalContinuations.Length; index++)
                {
                    if (group.NormalContinuations[index].Id != fallthrough)
                    {
                        VisitSequence(group.NormalContinuations[index].Body);
                    }
                }
            }
        }
    }
}
