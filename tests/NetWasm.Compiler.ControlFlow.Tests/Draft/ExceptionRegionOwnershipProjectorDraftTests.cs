using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Draft;

public sealed class ExceptionRegionOwnershipProjectorDraftTests
{
    [Fact]
    public void CollectWalksEveryStructuredContainerAndDeduplicatesGroups()
    {
        var source = CreateMethod();
        var block = CreateBlock();
        var bodyIfGroup = EmptyGroup(20);
        var bodyLoopGroup = EmptyGroup(30);
        var bodyPostTestLoopGroup = EmptyGroup(40);
        var bodyDispatcherGroup = EmptyGroup(50);
        var protectedGroup = EmptyGroup(60);
        var handlerGroup = EmptyGroup(70);
        var filterGroup = EmptyGroup(80);
        var continuationGroup = EmptyGroup(90);
        var continuationDispatcherGroup = EmptyGroup(100);
        var root = new StructuredExceptionGroupDraft(
            0,
            200,
            [
                new StructuredExceptionCodeDraft(new StructuredSequenceDraft([
                    new StructuredExceptionRegionDraft(protectedGroup, null),
                    new StructuredIfDraft(
                        block,
                        ExceptionRegion(bodyIfGroup),
                        StructuredSequenceDraft.Empty),
                    new StructuredLoopDraft(
                        block,
                        ContinueWhenConditionTrue: true,
                        ExceptionRegion(bodyLoopGroup),
                        StructuredSequenceDraft.Empty,
                        StructuredSequenceDraft.Empty),
                    new StructuredPostTestLoopDraft(
                        ExceptionRegion(bodyPostTestLoopGroup),
                        block,
                        ContinueWhenConditionTrue: false,
                        StructuredSequenceDraft.Empty,
                        StructuredSequenceDraft.Empty),
                    new StructuredDispatcherDraft(
                        EntryBlock: block.Index,
                        Blocks: [new StructuredDispatcherBlockDraft(block, null, null)],
                        Exits: [new StructuredDispatcherExitDraft(
                            block.Index,
                            ExceptionRegion(bodyDispatcherGroup))]),
                    new StructuredLoopBreakDraft(),
                    new StructuredLoopContinueDraft(),
                    new StructuredDispatcherContinueDraft(block.Index),
                    new UnknownRegion(),
                ])),
                new StructuredNestedExceptionGroupDraft(protectedGroup),
                new UnknownExceptionPart(),
            ],
            [
                new StructuredExceptionClauseDraft(
                    Region(110),
                    ExceptionRegion(handlerGroup),
                    ExceptionRegion(filterGroup)),
                new StructuredExceptionClauseDraft(
                    Region(120),
                    StructuredSequenceDraft.Empty,
                    null),
            ],
            [new StructuredExceptionContinuationDraft(
                130,
                ExceptionRegion(continuationGroup))])
        {
            ContinuationDispatcher = new StructuredDispatcherDraft(
                EntryBlock: block.Index,
                Blocks: [new StructuredDispatcherBlockDraft(block, null, null)],
                Exits: [new StructuredDispatcherExitDraft(
                    block.Index,
                    ExceptionRegion(continuationDispatcherGroup))]),
        };
        var method = source with
        {
            Body = new StructuredSequenceDraft([
                new StructuredExceptionRegionDraft(root, null),
                new StructuredIfDraft(
                    block,
                    ExceptionRegion(bodyIfGroup),
                    StructuredSequenceDraft.Empty),
                new StructuredLoopDraft(
                    block,
                    ContinueWhenConditionTrue: true,
                    StructuredSequenceDraft.Empty,
                    ExceptionRegion(bodyLoopGroup),
                    StructuredSequenceDraft.Empty),
                new StructuredPostTestLoopDraft(
                    StructuredSequenceDraft.Empty,
                    block,
                    ContinueWhenConditionTrue: false,
                    StructuredSequenceDraft.Empty,
                    ExceptionRegion(bodyPostTestLoopGroup)),
                new StructuredDispatcherDraft(
                    EntryBlock: block.Index,
                    Blocks: [new StructuredDispatcherBlockDraft(block, null, null)],
                    Exits: [new StructuredDispatcherExitDraft(
                        block.Index,
                        ExceptionRegion(bodyDispatcherGroup))]),
                new UnknownRegion(),
            ]),
            ExceptionGroups = [
                root,
                bodyIfGroup,
                bodyLoopGroup,
                bodyPostTestLoopGroup,
                bodyDispatcherGroup,
            ],
            BodyParts = [
                new StructuredExceptionCodeDraft(ExceptionRegion(protectedGroup)),
                new StructuredNestedExceptionGroupDraft(root),
                new UnknownExceptionPart(),
            ],
        };

        var collector = Assert.IsAssignableFrom<IExceptionGroupCollector>(
            new ExceptionGroupCollector());

        var result = collector.Collect(method);

        var expected = new[]
        {
            root,
            bodyIfGroup,
            bodyLoopGroup,
            bodyPostTestLoopGroup,
            bodyDispatcherGroup,
            protectedGroup,
            handlerGroup,
            filterGroup,
            continuationGroup,
            continuationDispatcherGroup,
        };
        Assert.Equal(expected.Length, result.Length);
        Assert.Equal(
            expected.Length,
            result.Count(candidate => expected.Any(group => ReferenceEquals(group, candidate))));
        Assert.All(expected, group => Assert.Contains(
            result,
            candidate => ReferenceEquals(candidate, group)));
    }

    [Fact]
    public void CollectRequiresAMethod()
    {
        var collector = Assert.IsAssignableFrom<IExceptionGroupCollector>(
            new ExceptionGroupCollector());

        Assert.Equal("method", Assert.Throws<ArgumentNullException>(
            () => collector.Collect(null!)).ParamName);
    }

    [Fact]
    public void BuildReturnsImmediateLexicalParentsAndNullForTopLevelGroups()
    {
        var outer = GroupWithRegion(0, 100, 150, 10);
        var inner = GroupWithRegion(10, 50, 70, 10);
        var grandchild = GroupWithRegion(20, 5, 30, 5);
        var sibling = GroupWithRegion(50, 10, 70, 5);
        var unrelated = GroupWithRegion(300, 10, 320, 5);
        var builder = Assert.IsAssignableFrom<IExceptionGroupParentMapBuilder>(
            new ExceptionGroupParentMapBuilder(new ExceptionScopeFinder()));

        var result = builder.Build([outer, inner, grandchild, sibling, unrelated]);

        Assert.Null(result[outer]);
        Assert.Same(outer, result[inner]);
        Assert.Same(inner, result[grandchild]);
        Assert.Same(outer, result[sibling]);
        Assert.Null(result[unrelated]);
    }

    [Fact]
    public void BuildReturnsAnEmptyMapForNoGroups()
    {
        var builder = Assert.IsAssignableFrom<IExceptionGroupParentMapBuilder>(
            new ExceptionGroupParentMapBuilder(new ExceptionScopeFinder()));

        var result = builder.Build([]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildRejectsMultipleImmediateLexicalParents()
    {
        var first = GroupWithRegion(0, 50, 80, 1);
        var second = GroupWithRegion(20, 50, 60, 10);
        var candidate = GroupWithRegion(25, 5, 35, 1);
        var builder = Assert.IsAssignableFrom<IExceptionGroupParentMapBuilder>(
            new ExceptionGroupParentMapBuilder(new ExceptionScopeFinder()));

        Assert.Throws<InvalidOperationException>(() => builder.Build([first, second, candidate]));
    }

    [Fact]
    public void ProjectCanonicalizesOccurrencesAcrossEveryStructuredNodeFamily()
    {
        var source = CreateMethod();
        var block = CreateBlock();
        var root = EmptyGroup(0);
        var child = EmptyGroup(10);
        var orphan = EmptyGroup(20);
        var detached = EmptyGroup(30);
        var simple = new StructuredSequenceDraft([new StructuredBlockDraft(block)]);
        var rootProtectedBody = new StructuredSequenceDraft([
            new StructuredExceptionRegionDraft(child, null),
            new StructuredExceptionRegionDraft(orphan, null),
            new StructuredIfDraft(block, simple, simple),
            new StructuredLoopDraft(block, true, simple, simple, simple),
            new StructuredPostTestLoopDraft(simple, block, false, simple, simple),
            new StructuredDispatcherDraft(
                block.Index,
                [new StructuredDispatcherBlockDraft(block, null, null)],
                [new StructuredDispatcherExitDraft(block.Index, simple)]),
            new StructuredLoopBreakDraft(),
            new StructuredLoopContinueDraft(),
            new StructuredDispatcherContinueDraft(block.Index),
            new UnknownRegion(),
        ]);
        root = root with
        {
            ProtectedParts = [
                new StructuredExceptionCodeDraft(rootProtectedBody),
                new StructuredNestedExceptionGroupDraft(child),
                new UnknownExceptionPart(),
            ],
            Clauses = [
                new StructuredExceptionClauseDraft(
                    Region(100),
                    ExceptionRegion(child),
                    ExceptionRegion(child)),
                new StructuredExceptionClauseDraft(
                    Region(110),
                    simple,
                    null),
            ],
            NormalContinuations = [new StructuredExceptionContinuationDraft(
                120,
                new StructuredSequenceDraft([
                    new StructuredExceptionRegionDraft(child, null),
                    new StructuredBlockDraft(block),
                ]))],
            ContinuationDispatcher = new StructuredDispatcherDraft(
                block.Index,
                [new StructuredDispatcherBlockDraft(block, null, null)],
                [new StructuredDispatcherExitDraft(
                    block.Index,
                    new StructuredSequenceDraft([
                        new StructuredExceptionRegionDraft(child, null),
                        new StructuredBlockDraft(block),
                    ]))]),
        };
        var method = source with
        {
            Body = new StructuredSequenceDraft([
                new StructuredExceptionRegionDraft(root, null),
                new StructuredExceptionRegionDraft(child, null),
                new StructuredExceptionRegionDraft(orphan, null),
                new StructuredIfDraft(block, simple, simple),
                new StructuredLoopDraft(block, true, simple, simple, simple),
                new StructuredPostTestLoopDraft(simple, block, false, simple, simple),
                new StructuredDispatcherDraft(
                    block.Index,
                    [new StructuredDispatcherBlockDraft(block, null, null)],
                    [new StructuredDispatcherExitDraft(block.Index, simple)]),
                new StructuredLoopBreakDraft(),
                new StructuredLoopContinueDraft(),
                new StructuredDispatcherContinueDraft(block.Index),
                new UnknownRegion(),
            ]),
            ExceptionGroups = [root, child, orphan, detached],
            BodyParts = [
                new StructuredExceptionCodeDraft(new StructuredSequenceDraft([
                    new StructuredExceptionRegionDraft(child, null),
                    new StructuredBlockDraft(block),
                ])),
                new StructuredNestedExceptionGroupDraft(child),
                new UnknownExceptionPart(),
            ],
        };
        var parents = new Dictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?>(
            ReferenceEqualityComparer.Instance)
        {
            [root] = null,
            [child] = root,
            [orphan] = child,
            [detached] = null,
        };
        var collector = new FixedGroupCollector([root, child, orphan, detached]);
        var parentMap = new FixedParentMapBuilder(parents);
        var projector = Assert.IsAssignableFrom<IExceptionRegionOwnershipProjectorDraft>(
            new ExceptionRegionOwnershipProjectorDraft(collector, parentMap));

        var projected = projector.Project(method);

        Assert.Equal(1, collector.CallCount);
        Assert.Same(method, collector.Method);
        Assert.Equal(1, parentMap.CallCount);
        Assert.Equal(4, parentMap.Groups.Length);
        var projectedRootRegion = Assert.IsType<StructuredExceptionRegionDraft>(
            projected.Body.Regions[0]);
        var topLevelExceptionRegions = projected.Body.Regions
            .OfType<StructuredExceptionRegionDraft>()
            .ToArray();
        Assert.Single(topLevelExceptionRegions);

        var projectedRoot = projectedRootRegion.Group;
        Assert.Same(projectedRoot, projected.ExceptionGroups[0]);
        Assert.Same(projectedRoot, projectedRootRegion.Group);
        Assert.Same(projectedRoot, topLevelExceptionRegions[0].Group);
        Assert.Same(projected.ExceptionGroups[1],
            Assert.IsType<StructuredNestedExceptionGroupDraft>(
                projectedRoot.ProtectedParts[1]).Group);

        var projectedCode = Assert.IsType<StructuredExceptionCodeDraft>(
            projectedRoot.ProtectedParts[0]);
        Assert.Contains(
            projectedCode.Body.Regions.OfType<StructuredExceptionRegionDraft>(),
            region => ReferenceEquals(region.Group, projected.ExceptionGroups[1]));
        Assert.DoesNotContain(
            projectedCode.Body.Regions.OfType<StructuredExceptionRegionDraft>(),
            region => ReferenceEquals(region.Group, orphan));
        Assert.Equal(9, projectedCode.Body.Regions.Length);
        Assert.IsType<StructuredIfDraft>(projectedCode.Body.Regions[1]);
        Assert.IsType<StructuredLoopDraft>(projectedCode.Body.Regions[2]);
        Assert.IsType<StructuredPostTestLoopDraft>(projectedCode.Body.Regions[3]);
        Assert.IsType<StructuredDispatcherDraft>(projectedCode.Body.Regions[4]);
        Assert.IsType<StructuredLoopBreakDraft>(projectedCode.Body.Regions[5]);
        Assert.IsType<StructuredLoopContinueDraft>(projectedCode.Body.Regions[6]);
        Assert.IsType<StructuredDispatcherContinueDraft>(projectedCode.Body.Regions[7]);
        Assert.IsType<UnknownRegion>(projectedCode.Body.Regions[8]);

        Assert.NotNull(projectedRoot.Clauses[0].FilterBody);
        Assert.Null(projectedRoot.Clauses[1].FilterBody);
        Assert.Single(projectedRoot.NormalContinuations);
        Assert.Empty(projectedRoot.NormalContinuations[0].Body.Regions.OfType<StructuredExceptionRegionDraft>());
        Assert.NotNull(projectedRoot.ContinuationDispatcher);
        Assert.Empty(projectedRoot.ContinuationDispatcher.Exits[0].Body.Regions.OfType<StructuredExceptionRegionDraft>());
        Assert.IsType<StructuredExceptionCodeDraft>(projected.BodyParts[0]);
        Assert.Empty(Assert.IsType<StructuredExceptionCodeDraft>(projected.BodyParts[0])
            .Body.Regions.OfType<StructuredExceptionRegionDraft>());
        Assert.IsType<StructuredNestedExceptionGroupDraft>(projected.BodyParts[1]);
        Assert.IsType<UnknownExceptionPart>(projected.BodyParts[2]);
        Assert.NotSame(detached, projected.ExceptionGroups[3]);
        Assert.Equal(detached.TryOffset, projected.ExceptionGroups[3].TryOffset);
    }

    [Fact]
    public void ProjectRequiresAMethod()
    {
        var projector = Assert.IsAssignableFrom<IExceptionRegionOwnershipProjectorDraft>(
            new ExceptionRegionOwnershipProjectorDraft(
                new FixedGroupCollector([]),
                new FixedParentMapBuilder(new Dictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?>(
                    ReferenceEqualityComparer.Instance))));

        Assert.Equal("method", Assert.Throws<ArgumentNullException>(
            () => projector.Project(null!)).ParamName);
    }

    internal static StructuredMethodDraft CreateMethod() =>
        new(default!, StructuredSequenceDraft.Empty, [], []);

    private static BasicBlock CreateBlock() =>
        new(
            0,
            0,
            [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())]);

    private static StructuredExceptionGroupDraft EmptyGroup(int offset) =>
        new(offset, 1, [], [], []);

    private static StructuredExceptionGroupDraft GroupWithRegion(
        int tryOffset,
        int tryLength,
        int handlerOffset,
        int handlerLength) =>
        new(
            tryOffset,
            tryLength,
            [],
            [new StructuredExceptionClauseDraft(
                Region(handlerOffset, handlerLength),
                StructuredSequenceDraft.Empty,
                null)],
            []);

    private static CilExceptionRegion Region(int handlerOffset, int handlerLength = 1) =>
        new(
            CilExceptionRegionKind.Catch,
            0,
            1,
            handlerOffset,
            handlerLength,
            null,
            null);

    private static StructuredSequenceDraft ExceptionRegion(
        StructuredExceptionGroupDraft group) =>
        new([new StructuredExceptionRegionDraft(group, null)]);

    private sealed record UnknownRegion : StructuredRegionDraft;

    private sealed record UnknownExceptionPart : StructuredExceptionPartDraft;

    private sealed class FixedGroupCollector(
        ImmutableArray<StructuredExceptionGroupDraft> groups) : IExceptionGroupCollector
    {
        public int CallCount { get; private set; }

        public StructuredMethodDraft? Method { get; private set; }

        public ImmutableArray<StructuredExceptionGroupDraft> Collect(StructuredMethodDraft method)
        {
            CallCount++;
            Method = method;
            return groups;
        }
    }

    private sealed class FixedParentMapBuilder(
        IReadOnlyDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?> parents) :
        IExceptionGroupParentMapBuilder
    {
        public int CallCount { get; private set; }

        public ImmutableArray<StructuredExceptionGroupDraft> Groups { get; private set; }

        public IReadOnlyDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?> Build(
            ImmutableArray<StructuredExceptionGroupDraft> exceptionGroups)
        {
            CallCount++;
            Groups = exceptionGroups;
            return parents;
        }
    }
}
