using System;
using NetWasm.Compiler.Core;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class StructuredControlFlowValidatorTests
{
    [Fact]
    public void ValidateAcceptsExactlyOneReachableBlockOwner()
    {
        var validated = CreateValidatedGraph();
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(validated.Graph.Entry)]),
            [],
            []);

        ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft()).Validate(method);
    }

    [Fact]
    public void ValidateAcceptsARepeatedRoutingOccurrence()
    {
        var validated = CreateValidatedGraph();
        var block = validated.Graph.Entry;
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredBlockDraft(block),
                new NwDraft.StructuredBlockDraft(block, false),
            ]),
            [],
            []);

        ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft()).Validate(method);
    }

    [Fact]
    public void ValidateTraversesRepeatedExceptionGroupsAndContinuationDispatchers()
    {
        var validated = CreateValidatedGraph();
        var block = validated.Graph.Entry;
        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [new NwDraft.StructuredExceptionCodeDraft(
                new NwDraft.StructuredSequenceDraft([
                    new NwDraft.StructuredBlockDraft(block),
                ]))],
            [],
            [])
        {
            ContinuationDispatcher = new NwDraft.StructuredDispatcherDraft(
                null,
                [],
                []),
        };
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredExceptionRegionDraft(group, null),
                new NwDraft.StructuredExceptionRegionDraft(group, null),
            ]),
            [group],
            [new NwDraft.StructuredExceptionCodeDraft(NwDraft.StructuredSequenceDraft.Empty)]);

        ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft())
            .Validate(method);
    }

    [Fact]
    public void ValidateRejectsMissingReachableBlocks()
    {
        var validated = CreateValidatedGraph();
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            NwDraft.StructuredSequenceDraft.Empty,
            [],
            []);

        Assert.Throws<InvalidOperationException>(() =>
            ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft()).Validate(method));
    }

    [Fact]
    public void ValidateRejectsMultipleOwners()
    {
        var validated = CreateValidatedGraph();
        var block = validated.Graph.Entry;
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredBlockDraft(block),
                new NwDraft.StructuredBlockDraft(block),
            ]),
            [],
            []);

        Assert.Throws<InvalidOperationException>(() =>
            ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft()).Validate(method));
    }

    [Fact]
    public void ValidateRejectsUnknownExceptionParts()
    {
        var validated = CreateValidatedGraph();
        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [new UnsupportedExceptionPartDraft()],
            [],
            []);
        var method = new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([
                new NwDraft.StructuredExceptionRegionDraft(group, null),
            ]),
            [group],
            []);

        Assert.Throws<InvalidOperationException>(() =>
            ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft())
                .Validate(method));
    }

    [Fact]
    public void ValidateRequiresAMethod()
    {
        Assert.Equal("method", Assert.Throws<ArgumentNullException>(
            () => ((NwDraft.IStructuredControlFlowValidatorDraft)new NwDraft.StructuredControlFlowValidatorDraft())
                .Validate(null!)).ParamName);
    }

    private static ValidatedControlFlowGraph CreateValidatedGraph() =>
        ControlFlowTestSupport.Validate(ControlFlowTestSupport.Body(
            CliValueKind.Void,
            0,
            [],
            ControlFlowTestSupport.I(0, CilOperation.Return)));

    private sealed record UnsupportedExceptionPartDraft :
        NwDraft.StructuredExceptionPartDraft;
}
