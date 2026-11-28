// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopRegionEligibilityClassifierTests
{
    [Theory]
    [InlineData("core")]
    [InlineData("domain")]
    [InlineData("header")]
    [InlineData("condition")]
    [InlineData("continue")]
    [InlineData("condition-exit")]
    [InlineData("exit")]
    [InlineData("body")]
    [InlineData("overlap")]
    [InlineData("unreachable")]
    public void ClassifyRejectsInvalidRegionBeforeConsultingCycleAnalysis(string invariant)
    {
        var request = CreateRequest(DefaultSuccessors());
        request = invariant switch
        {
            "core" => request with { Region = request.Region with { CoreComponent = [1] } },
            "domain" => request with { Domain = new(0, [0, 1, 3, 4, 5]) },
            "header" => request with { Region = request.Region with { Header = 0 } },
            "condition" => request with { Region = request.Region with { Condition = 0 } },
            "continue" => request with { Region = request.Region with { Continue = 0 } },
            "condition-exit" => request with { Region = request.Region with { ConditionExit = 1 } },
            "exit" => request with { Region = request.Region with { Exit = 1 } },
            "body" => request with { Region = request.Region with { BodyComponent = [1, 4] } },
            "overlap" => request with { Region = request.Region with { ConditionExitComponent = [3, 4] } },
            "unreachable" => request with
            {
                Graph = new(request.Graph.MethodBody, request.Graph.Blocks,
                    request.Graph.Successors, request.Graph.Predecessors,
                    request.Graph.ExceptionalSuccessors, request.Graph.ExceptionalPredecessors,
                    [0, 1, 2, 3, 5]),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invariant)),
        };
        var cycles = new UnexpectedCycleClassifier();

        Assert.False(new LoopRegionEligibilityClassifier(cycles).Classify(request));
    }

    [Theory]
    [InlineData("graph")]
    [InlineData("naturalLoop")]
    [InlineData("region")]
    [InlineData("domain")]
    public void ClassifyRejectsMissingRequiredInputsBeforeCycleAnalysis(string input)
    {
        var request = CreateRequest(DefaultSuccessors());
        request = input switch
        {
            "graph" => request with { Graph = null! },
            "naturalLoop" => request with { NaturalLoop = null! },
            "region" => request with { Region = null! },
            "domain" => request with { Domain = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

        Assert.Throws<ArgumentNullException>(() =>
            new LoopRegionEligibilityClassifier(new UnexpectedCycleClassifier()).Classify(request));
    }

    [Fact]
    public void ClassifyRejectsExceptionExtensionWithoutAnOwningDomain()
    {
        var request = CreateRequest(DefaultSuccessors(), domainBlocks: [0, 1, 2],
            hasExceptions: true,
            operations: ImmutableDictionary<int, CilOperation>.Empty.Add(2, CilOperation.Leave),
            additionalDomains: [new ControlFlowDomain(3, [3, 5])]);

        Assert.False(CreateActor().Classify(request));
    }

    private sealed class UnexpectedCycleClassifier : IControlFlowSubgraphCycleClassifier
    {
        public bool Classify(ControlFlowGraph graph, ImmutableHashSet<int> blocks) =>
            throw new InvalidOperationException("Invalid regions must be rejected before cycle analysis.");
    }

    [Fact]
    public void ClassifyAcceptsClosedAcyclicExtensionsInsideTheLoopDomain()
    {
        var request = CreateRequest(DefaultSuccessors());

        Assert.True(CreateActor().Classify(request));
    }

    [Fact]
    public void ClassifyRejectsAnExtensionOutsideANormalControlFlowDomain()
    {
        var request = CreateRequest(
            DefaultSuccessors(),
            domainBlocks: [0, 1, 2, 5]);

        Assert.False(CreateActor().Classify(request));
    }

    [Fact]
    public void ClassifyRejectsACyclicExtension()
    {
        var successors = DefaultSuccessors()
            .SetItem(3, [4])
            .SetItem(4, [3]);

        Assert.False(CreateActor().Classify(
            CreateRequest(successors)));
    }

    [Fact]
    public void ClassifyRejectsAnExtensionThatReentersTheNaturalLoop()
    {
        var successors = DefaultSuccessors().SetItem(4, [1]);

        Assert.False(CreateActor().Classify(
            CreateRequest(successors)));
    }

    [Fact]
    public void ClassifyAllowsAForeignEntryIntoAClosedAcyclicExtension()
    {
        var successors = DefaultSuccessors()
            .Add(6, [4]);

        Assert.True(CreateActor().Classify(
            CreateRequest(successors)));
    }

    [Fact]
    public void ClassifyRejectsAnExtensionThatEscapesPastTheSelectedExit()
    {
        var successors = DefaultSuccessors()
            .SetItem(4, [6])
            .Add(6, []);

        Assert.False(CreateActor().Classify(
            CreateRequest(successors)));
    }

    [Fact]
    public void ClassifyAllowsLeaveOwnedExtensionsAcrossExceptionDomains()
    {
        var request = CreateRequest(
            DefaultSuccessors(),
            domainBlocks: [0, 1, 2],
            hasExceptions: true,
            operations: ImmutableDictionary<int, CilOperation>.Empty
                .Add(2, CilOperation.Leave),
            additionalDomains: [new ControlFlowDomain(3, [3, 4, 5])]);

        Assert.True(CreateActor().Classify(request));
    }

    [Fact]
    public void ClassifyRejectsOrdinaryBranchesAcrossExceptionDomains()
    {
        var request = CreateRequest(
            DefaultSuccessors(),
            domainBlocks: [0, 1, 2],
            hasExceptions: true,
            additionalDomains: [new ControlFlowDomain(3, [3, 4, 5])]);

        Assert.False(CreateActor().Classify(request));
    }

    [Fact]
    public void ClassifyRejectsNullRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateActor().Classify(null!));
    }

    [Fact]
    public void ConstructorRejectsNullCycleClassifier()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LoopRegionEligibilityClassifier(null!));
    }

    private static LoopRegionEligibilityClassifier CreateActor() =>
        new(new ControlFlowSubgraphCycleClassifier(
            new ControlFlowComponentAnalyzer(),
            new ControlFlowCycleClassifier()));

    private static LoopRegionEligibilityRequest CreateRequest(
        ImmutableDictionary<int, ImmutableArray<int>> successors,
        ImmutableHashSet<int>? domainBlocks = null,
        bool hasExceptions = false,
        ImmutableDictionary<int, CilOperation>? operations = null,
        ImmutableArray<ControlFlowDomain> additionalDomains = default)
    {
        var nodeCount = successors.Keys
            .Concat(successors.Values.SelectMany(value => value))
            .Max() + 1;
        var normalizedSuccessors = Enumerable.Range(0, nodeCount)
            .ToImmutableDictionary(
                index => index,
                index => successors.GetValueOrDefault(index));
        var predecessors = Enumerable.Range(0, nodeCount)
            .ToImmutableDictionary(
                index => index,
                index => normalizedSuccessors
                    .Where(edge => edge.Value.Contains(index))
                    .Select(edge => edge.Key)
                    .ToImmutableArray());
        var blocks = Enumerable.Range(0, nodeCount)
            .Select(index => new BasicBlock(
                index,
                index,
                [new CilInstruction(
                    index,
                    index + 1,
                    operations?.GetValueOrDefault(index) ?? CilOperation.Nop,
                    new CilOperand.None())]))
            .ToImmutableArray();
        var templateBody = ControlFlowTestSupport.Body(
            CliValueKind.Void,
            0,
            [],
            ControlFlowTestSupport.I(0, CilOperation.Return));
        var body = hasExceptions
            ? templateBody with
            {
                ExceptionRegions =
                [new(
                    CilExceptionRegionKind.Catch,
                    0,
                    1,
                    1,
                    1,
                    ControlFlowTestSupport.TypeKey,
                    null)],
            }
            : templateBody;
        var emptyEdges = Enumerable.Range(0, nodeCount)
            .ToImmutableDictionary(
                index => index,
                _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(
            body,
            blocks,
            normalizedSuccessors,
            predecessors,
            emptyEdges,
            emptyEdges,
            Enumerable.Range(0, nodeCount).ToImmutableHashSet());
        var naturalLoop = ImmutableHashSet.Create(1, 2);
        var region = new LoopRegion(
            Header: 1,
            Condition: 2,
            Continue: 1,
            ConditionExit: 3,
            Exit: 5,
            CoreComponent: naturalLoop,
            BodyComponent: naturalLoop.Add(4),
            ConditionExitComponent: [3]);
        var domain = new ControlFlowDomain(
            0,
            domainBlocks ?? graph.ReachableBlocks);
        var domains = additionalDomains.IsDefault
            ? ImmutableArray.Create(domain)
            : [domain, .. additionalDomains];
        return new(graph, naturalLoop, region, domain, new ControlFlowDomains(domains));
    }

    private static ImmutableDictionary<int, ImmutableArray<int>>
        DefaultSuccessors() =>
        ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, [1])
            .Add(1, [2])
            .Add(2, [1, 3, 4])
            .Add(3, [5])
            .Add(4, [5])
            .Add(5, []);
}
