using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using System.Collections.Generic;
using System;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionAwareDispatcherShellBuilderTests
{
    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var dispatcher = new RecordingWholeRegionDispatcherBuilder();
        var blocks = new ExceptionAwareDispatcherBlockSelector();

        var dispatcherException = Assert.Throws<ArgumentNullException>(
            () => new ExceptionAwareDispatcherShellBuilder(null!, blocks));
        var blocksException = Assert.Throws<ArgumentNullException>(
            () => new ExceptionAwareDispatcherShellBuilder(dispatcher, null!));

        Assert.Equal("wholeRegionDispatchers", dispatcherException.ParamName);
        Assert.Equal("blocks", blocksException.ParamName);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(22)]
    [InlineData(99)]
    public void BuildPublishesTheDispatcherOwnershipProduct(int ownedBlock)
    {
        var dependency = new RecordingWholeRegionDispatcherBuilder();
        var builder = new ExceptionAwareDispatcherShellBuilder(dependency, new ExceptionAwareDispatcherBlockSelector());
        var input1 = ControlFlowStructuringStateTestFactory.Create();
        int? input2 = 21;
        ImmutableHashSet<int> input3 = ownedBlock == -1000 ? ImmutableHashSet<int>.Empty : ImmutableHashSet.Create(ownedBlock);
        ImmutableHashSet<int> input4 = ImmutableHashSet.Create(12, 99);
        int? input5 = 22;
        input1.ExceptionGroupsByEntry.Add(input2.GetValueOrDefault(), new StructuredExceptionGroupDraft(0, 0, ImmutableArray<StructuredExceptionPartDraft>.Empty, ImmutableArray<StructuredExceptionClauseDraft>.Empty, ImmutableArray<StructuredExceptionContinuationDraft>.Empty));
        input1.ExceptionGroupsByEntry.Add(101, new StructuredExceptionGroupDraft(21, 0, ImmutableArray<StructuredExceptionPartDraft>.Empty, ImmutableArray<StructuredExceptionClauseDraft>.Empty, ImmutableArray<StructuredExceptionContinuationDraft>.Empty));
        input1.ExceptionGroupsByEntry.Add(102, new StructuredExceptionGroupDraft(23, 0, ImmutableArray<StructuredExceptionPartDraft>.Empty, ImmutableArray<StructuredExceptionClauseDraft>.Empty, ImmutableArray<StructuredExceptionContinuationDraft>.Empty));

        var result = ((IExceptionAwareDispatcherShellBuilder)builder)
            .Build(input1, input2, input3, input4, input5);

        Assert.Equal(1, dependency.CallCount);
        Assert.Same(dependency.Result, result.Dispatcher);
        Assert.Same(dependency.CapturedOwnedBlocks, result.OwnedBlocks);
        Assert.True(result.OwnedBlocks.SetEquals(input3));
    }

    private sealed class RecordingWholeRegionDispatcherBuilder : IWholeRegionDispatcherBuilder
    {
        public StructuredDispatcherDraft Result { get; } = default!;

        public ImmutableHashSet<int>? CapturedOwnedBlocks { get; private set; }

        public int CallCount { get; private set; }

        public StructuredDispatcherDraft Build(ControlFlowStructuringState state, int? entry, ImmutableHashSet<int> component)
        {
            CallCount++;
            CapturedOwnedBlocks = component;
            return Result;
        }
    }
}
