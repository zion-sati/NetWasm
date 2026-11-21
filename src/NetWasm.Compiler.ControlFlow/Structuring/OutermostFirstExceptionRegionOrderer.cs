// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Orders exception regions so that a region comes before every region nested in its protected block, handler or
// filter. ECMA-335 lists nested regions first; a handler domain claimed in that order would take the code its nested
// catch leaves to, such as the continue target and exit of an enclosing handler's loop, from the handler that owns
// it. Regions at the same depth keep their metadata order.
internal sealed class OutermostFirstExceptionRegionOrderer : IExceptionRegionClaimOrderer
{
    public ImmutableArray<CilExceptionRegion> Order(ImmutableArray<CilExceptionRegion> regions) =>
        [.. regions
            .Select((region, index) => (Region: region, Index: index,
                Depth: regions.Count(outer => Encloses(outer, region))))
            .OrderBy(entry => entry.Depth)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Region)];

    private static bool Encloses(CilExceptionRegion outer, CilExceptionRegion inner) =>
        Contains(outer.TryOffset, outer.TryLength, inner) ||
        Contains(outer.HandlerOffset, outer.HandlerLength, inner) ||
        outer.FilterOffset is int filter && Contains(filter, outer.HandlerOffset - filter, inner);

    // A region's own protected block, shared by every handler of one try, does not enclose it.
    private static bool Contains(int offset, int length, CilExceptionRegion inner) =>
        inner.TryOffset >= offset &&
        inner.TryOffset + inner.TryLength <= offset + length &&
        (inner.TryOffset, inner.TryLength) != (offset, length);
}
