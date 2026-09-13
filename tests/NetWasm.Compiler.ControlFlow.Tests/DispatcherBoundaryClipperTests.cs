using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class DispatcherBoundaryClipperTests
{
    [Fact]
    public void ClipKeepsOnlyAllowedBlocksAndExcludesTheStopBoundary() =>
        AssertClipping(new DispatcherBoundaryClipper());

    [Fact]
    public void ClipRejectsMissingSets() =>
        AssertMissingSets(new DispatcherBoundaryClipper());

#pragma warning disable CA1859 // Contract tests deliberately dispatch through the capability interface.
    private static void AssertClipping(IDispatcherBoundaryClipper clipper)
    {
        var component = ImmutableHashSet.Create(1, 2, 3, 4);
        var allowed = ImmutableHashSet.Create(2, 3, 4, 5);

        Assert.Equal(
            ImmutableHashSet.Create(2, 3),
            clipper.Clip(component, allowed, 4));
        Assert.Equal(
            ImmutableHashSet.Create(2, 3, 4),
            clipper.Clip(component, allowed, null));
    }

    private static void AssertMissingSets(IDispatcherBoundaryClipper clipper)
    {
        var blocks = ImmutableHashSet<int>.Empty;

        Assert.Throws<ArgumentNullException>(() =>
            clipper.Clip(null!, blocks, null));
        Assert.Throws<ArgumentNullException>(() =>
            clipper.Clip(blocks, null!, null));
    }
#pragma warning restore CA1859

}
