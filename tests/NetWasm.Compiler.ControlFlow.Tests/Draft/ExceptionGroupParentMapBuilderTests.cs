using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests.Draft;

public sealed class ExceptionGroupParentMapBuilderTests
{
    [Fact]
    public void BuildReturnsAnEmptyMapForAnEmptyGroupSet()
    {
        NwDraft.IExceptionGroupParentMapBuilder builder = CreateBuilder();

        var result = builder.Build([]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildAssignsNullToRootAndUnrelatedGroups()
    {
        var root = Group(0, 20, 30, 1);
        var unrelated = Group(50, 10, 60, 1);
        NwDraft.IExceptionGroupParentMapBuilder builder = CreateBuilder();

        var result = builder.Build([root, unrelated]);

        Assert.Equal(2, result.Count);
        Assert.Null(result[root]);
        Assert.Null(result[unrelated]);
    }

    [Fact]
    public void BuildRecognizesProtectedFilterAndHandlerContainment()
    {
        var container = Group(0, 20, 30, 10, filterOffset: 20);
        var protectedChild = Group(1, 1, 2, 1);
        var filterChild = Group(21, 1, 22, 1);
        var handlerChild = Group(31, 1, 32, 1);
        NwDraft.IExceptionGroupParentMapBuilder builder = CreateBuilder();

        var result = builder.Build([container, protectedChild, filterChild, handlerChild]);

        Assert.Same(container, result[protectedChild]);
        Assert.Same(container, result[filterChild]);
        Assert.Same(container, result[handlerChild]);
        Assert.Null(result[container]);
    }

    [Fact]
    public void BuildSelectsTheImmediateParentFromAContainmentChain()
    {
        var outer = Group(0, 100, 200, 1);
        var middle = Group(10, 50, 60, 1);
        var inner = Group(20, 10, 30, 1);
        NwDraft.IExceptionGroupParentMapBuilder builder = CreateBuilder();

        var result = builder.Build([outer, middle, inner]);

        Assert.Null(result[outer]);
        Assert.Same(outer, result[middle]);
        Assert.Same(middle, result[inner]);
    }

    [Fact]
    public void BuildRejectsAGroupWithMultipleImmediateParents()
    {
        var firstContainer = Group(0, 1, 10, 10);
        var secondContainer = Group(5, 1, 10, 10);
        var candidate = Group(10, 1, 11, 1);
        NwDraft.IExceptionGroupParentMapBuilder builder = CreateBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build([firstContainer, secondContainer, candidate]));

        Assert.Contains("more than one immediate lexical parent", exception.Message);
    }

    private static NwDraft.IExceptionGroupParentMapBuilder CreateBuilder() =>
        Assert.IsAssignableFrom<NwDraft.IExceptionGroupParentMapBuilder>(
            new NwDraft.ExceptionGroupParentMapBuilder(new ExceptionScopeFinder()));

    private static NwDraft.StructuredExceptionGroupDraft Group(
        int tryOffset,
        int tryLength,
        int handlerOffset,
        int handlerLength,
        int? filterOffset = null) =>
        new(
            tryOffset,
            tryLength,
            [],
            [new NwDraft.StructuredExceptionClauseDraft(
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    tryOffset,
                    tryLength,
                    handlerOffset,
                    handlerLength,
                    null,
                    filterOffset),
                NwDraft.StructuredSequenceDraft.Empty,
                null)],
            []);
}
