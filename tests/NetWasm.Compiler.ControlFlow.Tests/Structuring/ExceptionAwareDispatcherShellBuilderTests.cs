// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        var loops = new RecordingDispatcherLoopIslandSelector();

        var dispatcherException = Assert.Throws<ArgumentNullException>(
            () => new ExceptionAwareDispatcherShellBuilder(null!, blocks, loops));
        var blocksException = Assert.Throws<ArgumentNullException>(
            () => new ExceptionAwareDispatcherShellBuilder(dispatcher, null!, loops));
        var loopsException = Assert.Throws<ArgumentNullException>(
            () => new ExceptionAwareDispatcherShellBuilder(dispatcher, blocks, null!));

        Assert.Equal("wholeRegionDispatchers", dispatcherException.ParamName);
        Assert.Equal("blocks", blocksException.ParamName);
        Assert.Equal("loopIslands", loopsException.ParamName);
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
        var loopIslands = new RecordingDispatcherLoopIslandSelector(
            ownedBlock == -1000 ? [] : [ownedBlock]);
        var builder = new ExceptionAwareDispatcherShellBuilder(
            dependency,
            new ExceptionAwareDispatcherBlockSelector(),
            loopIslands);
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
        Assert.Empty(result.OwnedBlocks);
        Assert.Equal(1, loopIslands.CallCount);
        Assert.Same(input1, loopIslands.CapturedState);
        Assert.Equal(input2, loopIslands.CapturedEntry);
        Assert.Same(input3, loopIslands.CapturedComponent);
        Assert.Same(input4, loopIslands.CapturedAllowed);
        Assert.True(loopIslands.CapturedDispatcherOwnedBlocks!.SetEquals(input3));
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

    private sealed class RecordingDispatcherLoopIslandSelector(
        ImmutableHashSet<int>? result = null) : IDispatcherLoopIslandSelector
    {
        private readonly ImmutableHashSet<int> _result = result ?? [];

        public int CallCount { get; private set; }
        public ControlFlowStructuringState? CapturedState { get; private set; }
        public int? CapturedEntry { get; private set; }
        public ImmutableHashSet<int>? CapturedComponent { get; private set; }
        public ImmutableHashSet<int>? CapturedAllowed { get; private set; }
        public ImmutableHashSet<int>? CapturedDispatcherOwnedBlocks { get; private set; }

        public ImmutableHashSet<int> Select(
            ControlFlowStructuringState state,
            int? entry,
            ImmutableHashSet<int> component,
            ImmutableHashSet<int> allowed,
            ImmutableHashSet<int> dispatcherOwnedBlocks)
        {
            CallCount++;
            CapturedState = state;
            CapturedEntry = entry;
            CapturedComponent = component;
            CapturedAllowed = allowed;
            CapturedDispatcherOwnedBlocks = dispatcherOwnedBlocks;
            return _result;
        }
    }
}
