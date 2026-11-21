// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// Handler domains are claimed outermost first, so a catch nested in a finally's loop does not take the loop's continue
// target and exit from the finally (issue #259's siblings, a break or continue out of the nested catch).
public sealed class OutermostFirstExceptionRegionOrdererTests
{
    private static readonly OutermostFirstExceptionRegionOrderer Orderer = new();

    [Fact]
    public void ARegionNestedInAHandlerFollowsTheRegionItIsIn()
    {
        // try 0-10 / finally 10-40; inside the finally, try 15-20 / catch 20-25, listed first as ECMA-335 requires.
        var nested = Region(CilExceptionRegionKind.Catch, 15, 5, 20, 5);
        var outer = Region(CilExceptionRegionKind.Finally, 0, 10, 10, 30);

        Assert.Equal([outer, nested], Assert.IsAssignableFrom<IExceptionRegionClaimOrderer>(Orderer).Order([nested, outer]));
    }

    [Fact]
    public void ARegionNestedInAProtectedBlockOrAFilterFollowsTheRegionItIsIn()
    {
        var inTry = Region(CilExceptionRegionKind.Catch, 2, 3, 5, 2);
        var aroundTry = Region(CilExceptionRegionKind.Finally, 0, 10, 10, 5);
        var inFilter = Region(CilExceptionRegionKind.Catch, 22, 2, 24, 2);
        var aroundFilter = Region(CilExceptionRegionKind.Filter, 15, 5, 30, 5) with { FilterOffset = 20 };

        Assert.Equal([aroundTry, aroundFilter, inTry, inFilter], Orderer.Order([inTry, aroundTry, inFilter, aroundFilter]));
    }

    [Fact]
    public void HandlersOfOneTryAndSiblingRegionsKeepTheirOrder()
    {
        var first = Region(CilExceptionRegionKind.Catch, 0, 10, 10, 5);
        var second = Region(CilExceptionRegionKind.Catch, 0, 10, 15, 5);
        var sibling = Region(CilExceptionRegionKind.Finally, 30, 5, 35, 5);

        Assert.Equal([first, second, sibling], Orderer.Order([first, second, sibling]));
        Assert.Empty(Orderer.Order([]));
    }

    private static CilExceptionRegion Region(CilExceptionRegionKind kind, int tryOffset, int tryLength,
        int handlerOffset, int handlerLength) =>
        new(kind, tryOffset, tryLength, handlerOffset, handlerLength, null, null);
}
