using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class StructuredControlFlowOwnershipResolverTests
{
    [Fact]
    public void ResolveSelectsOnePreferredOccurrencePerReachableBlock()
    {
        var validated = CreateValidatedGraph();
        var block = validated.Graph.Entry;
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredBlockDraft(block, false),
                new NwDraft.StructuredBlockDraft(block),
                new NwDraft.StructuredBlockDraft(block),
            ]),
            [],
            []);

        var result = Resolve(method);
        var blocks = result.Body.Regions.Cast<NwDraft.StructuredBlockDraft>().ToArray();

        Assert.False(blocks[0].IsOriginal);
        Assert.True(blocks[1].IsOriginal);
        Assert.False(blocks[2].IsOriginal);
    }

    [Fact]
    public void ResolveSelectsTheFirstOccurrenceWhenNoneIsPreferred()
    {
        var validated = CreateValidatedGraph();
        var block = validated.Graph.Entry;
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredBlockDraft(block, false),
                new NwDraft.StructuredBlockDraft(block, false),
            ]),
            [],
            []);

        var result = Resolve(method);
        var blocks = result.Body.Regions.Cast<NwDraft.StructuredBlockDraft>().ToArray();

        Assert.True(blocks[0].IsOriginal);
        Assert.False(blocks[1].IsOriginal);
    }

    [Fact]
    public void ResolveIsIdempotent()
    {
        var validated = CreateValidatedGraph();
        var block = validated.Graph.Entry;
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredBlockDraft(block, false),
                new NwDraft.StructuredBlockDraft(block),
            ]),
            [],
            []);

        var once = Resolve(method);
        var twice = Resolve(once);

        Assert.Equal(
            once.Body.Regions.Cast<NwDraft.StructuredBlockDraft>().Select(item => item.IsOriginal),
            twice.Body.Regions.Cast<NwDraft.StructuredBlockDraft>().Select(item => item.IsOriginal));
    }

    [Fact]
    public void ResolveRequiresAMethod()
    {
        Assert.Equal("method", Assert.Throws<ArgumentNullException>(
            () => Resolve(null!)).ParamName);
    }

    [Fact]
    public void ResolveRejectsAnUnknownExceptionPart()
    {
        var validated = CreateValidatedGraph();
        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [new UnknownExceptionPart()],
            [],
            []);
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredExceptionRegionDraft(group, null)]),
            [group],
            []);

        Assert.Throws<InvalidOperationException>(() => Resolve(method));
    }

    private static NwDraft.StructuredMethodDraft Resolve(NwDraft.StructuredMethodDraft method) =>
        ((NwDraft.IStructuredControlFlowOwnershipResolverDraft)new NwDraft.StructuredControlFlowOwnershipResolverDraft(
            new NwDraft.StructuredControlFlowOccurrenceCollector(),
            new NwDraft.StructuredControlFlowOwnerSelector(),
            new NwDraft.StructuredControlFlowOwnershipProjector()))
            .Resolve(method);

    private static ValidatedControlFlowGraph CreateValidatedGraph() =>
        ControlFlowTestSupport.Validate(ControlFlowTestSupport.Body(
            CliValueKind.Void,
            0,
            [],
            ControlFlowTestSupport.I(0, CilOperation.Return)));

    private sealed record UnknownExceptionPart : NwDraft.StructuredExceptionPartDraft;
}
