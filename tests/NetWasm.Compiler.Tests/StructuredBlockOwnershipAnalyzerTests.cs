using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class StructuredBlockOwnershipAnalyzerTests
{
    [Fact]
    public void AnalyzeTraversesEveryOwnedRegionAndSkipsSyntheticRepresentations()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = original.Blocks[original.EntryBlock];
        var leaf = new StructuredSequence([
            new StructuredCode(new StructuredBlockOccurrence(
                block.Id,
                StructuredBlockRole.Owner)),
        ]);
        var nested = new StructuredExceptionGroup(
            new StructuredExceptionGroupId(1),
            new StructuredExceptionGroupId(0),
            block.StartOffset,
            block.EndOffset - block.StartOffset,
            [new StructuredExceptionCode(leaf)],
            [],
            [],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var clause = new StructuredExceptionClause(
            CilExceptionRegionKind.Filter,
            block.StartOffset,
            block.EndOffset - block.StartOffset,
            null,
            block.StartOffset,
            leaf,
            leaf)
        {
            HandlerBlock = block.Id,
            FilterBlock = block.Id,
        };
        var group = new StructuredExceptionGroup(
            new StructuredExceptionGroupId(0),
            null,
            block.StartOffset,
            block.EndOffset - block.StartOffset,
            [
                new StructuredExceptionCode(leaf),
                new StructuredNestedExceptionGroup(nested.Id),
            ],
            [clause],
            [new StructuredExceptionContinuation(
                new StructuredContinuationId(0),
                block.StartOffset,
                block.Id,
                leaf)],
            null,
            null)
        {
            ProtectedBlocks = [block.Id],
        };
        var body = new StructuredSequence([
            new StructuredCode(new StructuredBlockOccurrence(
                block.Id,
                StructuredBlockRole.Owner)),
            new StructuredCode(new StructuredBlockOccurrence(
                block.Id,
                StructuredBlockRole.ExecutingReplica)),
            new StructuredIf(
                new StructuredBlockOccurrence(block.Id, StructuredBlockRole.Owner),
                leaf,
                StructuredSequence.Empty),
            new StructuredLoop(
                new StructuredBlockOccurrence(block.Id, StructuredBlockRole.Owner),
                true,
                leaf,
                leaf,
                leaf),
            new StructuredPostTestLoop(
                leaf,
                new StructuredBlockOccurrence(block.Id, StructuredBlockRole.Owner),
                false,
                leaf,
                leaf),
            new StructuredExceptionRegion(group.Id, null),
            new StructuredExceptionRegion(group.Id, null),
            new StructuredDispatcher(
                block.Id,
                [
                    new StructuredDispatcherBlock(
                        new StructuredBlockOccurrence(block.Id, StructuredBlockRole.Owner),
                        null,
                        null),
                    new StructuredDispatcherBlock(
                        new StructuredBlockOccurrence(block.Id, StructuredBlockRole.ExecutingReplica),
                        null,
                        null),
                ],
                [new StructuredDispatcherExit(block.Id, leaf)]),
            new StructuredLoopBreak(),
            new StructuredLoopContinue(),
            new StructuredDispatcherContinue(block.Id),
            new StructuredLabeledBlock(block.Id, leaf),
            new StructuredLabeledLoop(block.Id, new([new StructuredLabelBranch(block.Id)])),
        ]);
        var analyzer = Assert.IsAssignableFrom<IStructuredBlockOwnershipAnalyzer>(
            new StructuredBlockOwnershipAnalyzer());

        var visits = analyzer.Analyze(original with
        {
            Body = body,
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = new Dictionary<StructuredExceptionGroupId, StructuredExceptionGroup>
            {
                [group.Id] = group,
                [nested.Id] = nested,
            }.ToImmutableDictionary(),
        });

        Assert.NotEmpty(visits);
        Assert.All(visits, visit => Assert.Equal(block.Id.Value, visit.BlockIndex));
        Assert.Null(visits[0].FirstPath);
        Assert.All(visits[1..], visit => Assert.Equal(visits[0].Path, visit.FirstPath));
        Assert.DoesNotContain(visits, visit => visit.Path.Contains("Blocks[1]", StringComparison.Ordinal));
        Assert.Contains(visits, visit => visit.Path.EndsWith(".Body.Regions[0]", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeRejectsNullAndUnknownStructuredShapes()
    {
        var analyzer = Assert.IsAssignableFrom<IStructuredBlockOwnershipAnalyzer>(
            new StructuredBlockOwnershipAnalyzer());

        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(null!));

        var original = ValidationTestData.StructuredMethod();
        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(
            original with
            {
                Body = new StructuredSequence([new UnsupportedStructuredRegion()]),
            }));

        var unsupportedGroup = new StructuredExceptionGroup(
            new StructuredExceptionGroupId(0),
            null,
            original.Blocks[original.EntryBlock].StartOffset,
            1,
            [new UnsupportedExceptionPart()],
            [],
            [],
            null,
            null)
        {
            ProtectedBlocks = [original.EntryBlock],
        };
        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(
            original with
            {
                Body = new StructuredSequence(
                    [new StructuredExceptionRegion(unsupportedGroup.Id, null)]),
                TopLevelExceptionGroups = [unsupportedGroup.Id],
                ExceptionGroups = new Dictionary<StructuredExceptionGroupId, StructuredExceptionGroup>
                {
                    [unsupportedGroup.Id] = unsupportedGroup,
                }.ToImmutableDictionary(),
            }));
    }

    private sealed record UnsupportedStructuredRegion : StructuredRegion;

    private sealed record UnsupportedExceptionPart : StructuredExceptionPart;
}
