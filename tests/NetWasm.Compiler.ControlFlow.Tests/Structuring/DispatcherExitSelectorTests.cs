using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class DispatcherExitSelectorTests
{
    [Fact]
    public void SelectsOnlyAllowedSuccessors()
    {
        var selector = new DispatcherExitSelector();
        int[] successors = [1, 2, 3];
        HashSet<int> allowed = [2, 3, 4];

        var result = Select(selector, successors, allowed);

        Assert.Equal([2, 3], result.Order());
    }

    [Fact]
    public void RejectsNullSuccessors()
    {
        var selector = new DispatcherExitSelector();

        Assert.Throws<ArgumentNullException>(() =>
            Select(selector, null!, []));
    }

    [Fact]
    public void RejectsNullAllowedSet()
    {
        var selector = new DispatcherExitSelector();

        Assert.Throws<ArgumentNullException>(() =>
            Select(selector, [], null!));
    }

    private static int[] Select<TSelector>(
        TSelector selector,
        IEnumerable<int> successors,
        IEnumerable<int> allowed)
        where TSelector : IDispatcherExitSelector =>
        selector.Select(successors, allowed);
}
