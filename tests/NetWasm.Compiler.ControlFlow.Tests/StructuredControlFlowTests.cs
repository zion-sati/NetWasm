using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class StructuredControlFlowTests
{
    [Fact]
    public void StructuredRegionsExposeTheirContractData()
    {
        var method = ControlFlowTestSupport.Body(
            CliValueKind.Void,
            0,
            [],
            ControlFlowTestSupport.I(0, CilOperation.Return));
        var graph = ControlFlowTestSupport.CreateGraphBuilder().Build(method);
        var block = graph.Entry;
        var sequence = new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(block)]);
        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [new NwDraft.StructuredExceptionCodeDraft(sequence)],
            [],
            []);
        var dispatcher = new NwDraft.StructuredDispatcherDraft(
            0,
            [new NwDraft.StructuredDispatcherBlockDraft(block, 0, null)],
            [new NwDraft.StructuredDispatcherExitDraft(0, sequence)]);
        var postTestLoop = new NwDraft.StructuredPostTestLoopDraft(
            sequence,
            block,
            true,
            sequence,
            sequence);

        Assert.Equal(0, new NwDraft.StructuredDispatcherContinueDraft(0).TargetBlock);
        Assert.Equal(0, dispatcher.EntryBlock);
        Assert.Single(dispatcher.Blocks);
        Assert.Single(dispatcher.Exits);
        Assert.Equal(0, dispatcher.Blocks[0].WhenTrue);
        Assert.True(postTestLoop.ContinueWhenConditionTrue);
        Assert.Same(sequence, postTestLoop.ContinueBody);
        Assert.Same(sequence, postTestLoop.ExitBody);
        Assert.Same(group.ProtectedParts[0], group.ProtectedParts[0]);
        Assert.Equal(0, group.TryOffset);
        Assert.Equal(1, group.TryLength);
    }
}
