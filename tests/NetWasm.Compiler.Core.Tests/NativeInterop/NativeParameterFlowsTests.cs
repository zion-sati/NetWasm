// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeParameterFlowsTests
{
    // A parameter past the declared ones, or any parameter of an unset list, takes the default flow.
    [Fact]
    public void AParameterWithoutADeclaredFlowHasNone()
    {
        var flows = new NativeParameterFlows([NativeParameterFlow.In, NativeParameterFlow.In | NativeParameterFlow.Out]);

        Assert.Equal(NativeParameterFlow.In, flows[0]);
        Assert.Equal(NativeParameterFlow.In | NativeParameterFlow.Out, flows[1]);
        Assert.Equal(NativeParameterFlow.None, flows[2]);
        Assert.Equal(NativeParameterFlow.None, new NativeParameterFlows(default)[0]);
        Assert.Equal(NativeParameterFlow.None, NativeParameterFlows.None[0]);
    }

    // Equal declarations read twice compare and hash equal; a different flow or list does not.
    [Fact]
    public void FlowsCompareAndHashByValue()
    {
        var first = new NativeParameterFlows([NativeParameterFlow.Out, NativeParameterFlow.None]);
        var second = new NativeParameterFlows(ImmutableArray.Create(NativeParameterFlow.Out, NativeParameterFlow.None));
        var other = new NativeParameterFlows([NativeParameterFlow.In, NativeParameterFlow.None]);

        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(other));
        Assert.False(first.Equals((NativeParameterFlows?)null));
        Assert.True(NativeParameterFlows.None.Equals(new NativeParameterFlows([])));
        Assert.Equal(NativeParameterFlows.None.GetHashCode(), new NativeParameterFlows([]).GetHashCode());
    }
}
