using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ActiveControlFlowBlockClipperTests
{
    [Fact]
    public void ClipRemovesEveryActiveBlock()
    {
        var clipper = Assert.IsAssignableFrom<IActiveControlFlowBlockClipper>(
            new ActiveControlFlowBlockClipper());
        var allowed = ImmutableHashSet.Create(2, 3, 5, 8);
        IReadOnlySet<int> activePath = new HashSet<int> { 2, 3, 13 };

        var clipped = clipper.Clip(allowed, activePath, ImmutableHashSet<int>.Empty);

        Assert.Equal([5, 8], clipped);
        Assert.Equal([2, 3, 5, 8], allowed);
    }

    [Fact]
    public void ClipDoesNotAddActiveBlocksOutsideAllowedDomain()
    {
        var clipper = Assert.IsAssignableFrom<IActiveControlFlowBlockClipper>(
            new ActiveControlFlowBlockClipper());
        var allowed = ImmutableHashSet.Create(5, 8);
        IReadOnlySet<int> activePath = new HashSet<int> { 2, 3 };

        var clipped = clipper.Clip(allowed, activePath, ImmutableHashSet<int>.Empty);

        Assert.Equal([5, 8], clipped);
    }

    [Fact]
    public void ClipRejectsNullAllowedDomain()
    {
        var clipper = Assert.IsAssignableFrom<IActiveControlFlowBlockClipper>(
            new ActiveControlFlowBlockClipper());

        Assert.Throws<ArgumentNullException>(() =>
            clipper.Clip(null!, new HashSet<int>(), ImmutableHashSet<int>.Empty));
    }

    [Fact]
    public void ClipRejectsNullActivePath()
    {
        var clipper = Assert.IsAssignableFrom<IActiveControlFlowBlockClipper>(
            new ActiveControlFlowBlockClipper());

        Assert.Throws<ArgumentNullException>(() =>
            clipper.Clip(ImmutableHashSet<int>.Empty, null!, ImmutableHashSet<int>.Empty));
    }
    [Fact]
    public void ClipRetainsActiveBlocksOwnedByCurrentRegion()
    {
        var clipper = Assert.IsAssignableFrom<IActiveControlFlowBlockClipper>(
            new ActiveControlFlowBlockClipper());
        var allowed = ImmutableHashSet.Create(2, 3);

        var clipped = clipper.Clip(
            allowed,
            ImmutableHashSet.Create(2),
            ImmutableHashSet.Create(2));

        Assert.Equal(allowed, clipped);
    }

    [Fact]
    public void ClipRejectsNullRetainedDomain()
    {
        var clipper = Assert.IsAssignableFrom<IActiveControlFlowBlockClipper>(
            new ActiveControlFlowBlockClipper());

        Assert.Throws<ArgumentNullException>(() => clipper.Clip(
            ImmutableHashSet<int>.Empty,
            ImmutableHashSet<int>.Empty,
            null!));
    }

}
