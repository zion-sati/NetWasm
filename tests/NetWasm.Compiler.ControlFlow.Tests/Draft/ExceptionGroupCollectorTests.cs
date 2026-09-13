using System;
using System.Collections.Generic;
using System.Linq;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests.Draft;

public sealed class ExceptionGroupCollectorTests
{
    [Fact]
    public void CollectRequiresAMethod()
    {
        var collector = Assert.IsAssignableFrom<NwDraft.IExceptionGroupCollector>(
            new NwDraft.ExceptionGroupCollector());

        var exception = Assert.Throws<ArgumentNullException>(() => collector.Collect(null!));

        Assert.Equal("method", exception.ParamName);
    }

    [Fact]
    public void CollectReturnsNoGroupsForAnEmptyMethod()
    {
        var collector = Assert.IsAssignableFrom<NwDraft.IExceptionGroupCollector>(
            new NwDraft.ExceptionGroupCollector());
        var method = Method(NwDraft.StructuredSequenceDraft.Empty);

        var result = collector.Collect(method);

        Assert.Empty(result);
    }

    [Fact]
    public void CollectTraversesEveryStructuredExceptionContainerAndDeduplicatesByIdentity()
    {
        var protectedCodeGroup = Group();
        var nestedGroup = Group();
        var nestedExceptionRegionGroup = Group();
        var conditionalGroup = Group();
        var loopGroup = Group();
        var postTestLoopGroup = Group();
        var dispatcherGroup = Group();
        var handlerGroup = Group();
        var filterGroup = Group();
        var continuationGroup = Group();
        var methodBodyGroup = Group();

        var protectedBody = Sequence(
            new NwDraft.StructuredExceptionRegionDraft(nestedExceptionRegionGroup, null),
            new NwDraft.StructuredIfDraft(
                default!,
                Sequence(new NwDraft.StructuredExceptionRegionDraft(conditionalGroup, null)),
                NwDraft.StructuredSequenceDraft.Empty),
            new NwDraft.StructuredLoopDraft(
                default!,
                true,
                Sequence(new NwDraft.StructuredExceptionRegionDraft(loopGroup, null)),
                NwDraft.StructuredSequenceDraft.Empty,
                NwDraft.StructuredSequenceDraft.Empty),
            new NwDraft.StructuredPostTestLoopDraft(
                Sequence(new NwDraft.StructuredExceptionRegionDraft(postTestLoopGroup, null)),
                default!,
                false,
                NwDraft.StructuredSequenceDraft.Empty,
                NwDraft.StructuredSequenceDraft.Empty),
            new NwDraft.StructuredDispatcherDraft(
                null,
                [],
                [new NwDraft.StructuredDispatcherExitDraft(
                    0,
                    Sequence(new NwDraft.StructuredExceptionRegionDraft(dispatcherGroup, null))) ]),
            new NwDraft.StructuredBlockDraft(default!),
            new NwDraft.StructuredLoopBreakDraft(),
            new NwDraft.StructuredLoopContinueDraft(),
            new NwDraft.StructuredDispatcherContinueDraft(0));

        protectedCodeGroup = protectedCodeGroup with
        {
            ProtectedParts =
            [
                new NwDraft.StructuredExceptionCodeDraft(protectedBody),
                new NwDraft.StructuredNestedExceptionGroupDraft(nestedGroup),
            ],
            Clauses =
            [
                Clause(
                    Sequence(new NwDraft.StructuredExceptionRegionDraft(handlerGroup, null)),
                    Sequence(new NwDraft.StructuredExceptionRegionDraft(filterGroup, null))),
                Clause(NwDraft.StructuredSequenceDraft.Empty, null),
            ],
            NormalContinuations =
            [new NwDraft.StructuredExceptionContinuationDraft(
                0,
                Sequence(new NwDraft.StructuredExceptionRegionDraft(continuationGroup, null)))],
        };

        var method = new NwDraft.StructuredMethodDraft(
            default!,
            Sequence(new NwDraft.StructuredExceptionRegionDraft(methodBodyGroup, null)),
            [protectedCodeGroup, protectedCodeGroup],
            []);
        var collector = Assert.IsAssignableFrom<NwDraft.IExceptionGroupCollector>(
            new NwDraft.ExceptionGroupCollector());

        var result = collector.Collect(method);

        var expected = new[]
        {
            protectedCodeGroup,
            nestedExceptionRegionGroup,
            conditionalGroup,
            loopGroup,
            postTestLoopGroup,
            dispatcherGroup,
            nestedGroup,
            handlerGroup,
            filterGroup,
            continuationGroup,
            methodBodyGroup,
        };
        Assert.Equal(expected.Length, result.Length);
        Assert.All(expected, expectedGroup =>
            Assert.Contains(result, actualGroup => ReferenceEquals(expectedGroup, actualGroup)));
        Assert.Equal(expected.Length, result.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    private static NwDraft.StructuredMethodDraft Method(NwDraft.StructuredSequenceDraft body) =>
        new(default!, body, [], []);

    private static NwDraft.StructuredSequenceDraft Sequence(
        params NwDraft.StructuredRegionDraft[] regions) =>
        new([.. regions]);

    internal static NwDraft.StructuredExceptionGroupDraft Group() =>
        new(0, 1, [], [], []);

    internal static NwDraft.StructuredExceptionClauseDraft Clause(
        NwDraft.StructuredSequenceDraft handler,
        NwDraft.StructuredSequenceDraft? filter) =>
        new(default!, handler, filter);

    private sealed class ReferenceEqualityComparer : IEqualityComparer<NwDraft.StructuredExceptionGroupDraft>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(
            NwDraft.StructuredExceptionGroupDraft? left,
            NwDraft.StructuredExceptionGroupDraft? right) =>
            ReferenceEquals(left, right);

        public int GetHashCode(NwDraft.StructuredExceptionGroupDraft obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
