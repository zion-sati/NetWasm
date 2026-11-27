// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests;

// The index answers what a scan over the method's regions and blocks answers: regions starting at a block, the blocks
// a range holds, whether the ranges around a block hold an offset too, and every region's start, with blocks out of
// offset order, regions at offsets no block starts at, and regions that overlap without nesting.
public sealed class ExceptionRegionBlocksTests
{
    [Fact]
    public void AnswersAsAScanOverTheRegionsAndBlocksDoes()
    {
        var random = new Random(20261122);
        for (var round = 0; round < 100; round++)
        {
            var offsets = Enumerable.Range(0, 60).Where(_ => random.Next(3) == 0).OrderBy(_ => random.Next()).ToArray();
            ImmutableArray<BasicBlock> blocks = [.. offsets.Select((offset, index) => new BasicBlock(index, offset, []))];
            var regions = Enumerable.Range(0, random.Next(0, 6)).Select(_ => new CilExceptionRegion(
                CilExceptionRegionKind.Catch, random.Next(60), random.Next(1, 20), random.Next(60), random.Next(1, 20),
                null, random.Next(4) == 0 ? random.Next(60) : null)).ToImmutableArray();
            var body = ControlFlowTestSupport.Body(CliValueKind.Void, 0, [], ControlFlowTestSupport.I(0, CilOperation.Return)) with
            {
                ExceptionRegions = regions,
            };
            var index = new ExceptionRegionBlocks(body, blocks);

            foreach (var block in blocks)
            {
                Assert.Equal(
                    regions.Where(region => region.TryOffset == block.StartOffset),
                    index.TryRegionsStartingAt(block.Index));
                for (var offset = 0; offset < 70; offset += 3)
                {
                    Assert.Equal(AroundAlsoHold(regions, block.StartOffset, offset), index.RangesAroundAlsoHold(block.Index, offset));
                }
            }
            for (var start = 0; start < 70; start += 7)
            {
                Assert.Equal(
                    blocks.Where(block => block.StartOffset >= start && block.StartOffset < start + 9).OrderBy(block => block.StartOffset).Select(block => block.Index),
                    index.BlocksFrom(start, 9));
            }
            Assert.True(index.RegionStartOffsets.SetEquals(regions.SelectMany(region =>
                region.FilterOffset is int filter ? new[] { region.TryOffset, region.HandlerOffset, filter } : [region.TryOffset, region.HandlerOffset])));
        }
    }

    // Nested regions, as valid IL has them: a protected block and its handler side by side, nested in an outer
    // protected block or handler, some sharing a protected block. The innermost range around a block decides.
    [Fact]
    public void NestedRegionsAnswerFromTheInnermostRangeAroundABlock()
    {
        var random = new Random(11);
        for (var round = 0; round < 100; round++)
        {
            var regions = new List<CilExceptionRegion>();
            Nest(0, 64, random.Next(1, 4));
            ImmutableArray<BasicBlock> blocks = [.. Enumerable.Range(0, 64).Where(offset => offset % 2 == 0 || random.Next(2) == 0)
                .Select((offset, index) => new BasicBlock(index, offset, []))];
            var body = ControlFlowTestSupport.Body(CliValueKind.Void, 0, [], ControlFlowTestSupport.I(0, CilOperation.Return)) with
            {
                ExceptionRegions = [.. regions],
            };
            var index = new ExceptionRegionBlocks(body, blocks);

            foreach (var block in blocks)
            {
                for (var offset = 0; offset < 64; offset++)
                {
                    Assert.Equal(AroundAlsoHold([.. regions], block.StartOffset, offset), index.RangesAroundAlsoHold(block.Index, offset));
                }
            }

            // A protected block and its handler in [start, end), each nesting more regions.
            void Nest(int start, int end, int depth)
            {
                if (depth == 0 || end - start < 4)
                {
                    return;
                }
                var middle = random.Next(start + 1, end - 1);
                regions.Add(new(CilExceptionRegionKind.Catch, start, middle - start, middle, end - middle, null, null));
                if (random.Next(3) == 0)
                {
                    regions.Add(new(CilExceptionRegionKind.Finally, start, middle - start, middle, end - middle, null, null));
                }
                Nest(start + 1, middle, depth - 1);
                Nest(middle + 1, end, depth - 1);
            }
        }
    }

    private static bool AroundAlsoHold(ImmutableArray<CilExceptionRegion> regions, int blockStart, int offset) =>
        regions.All(region =>
            (!Holds(region.TryOffset, region.TryLength, blockStart) || Holds(region.TryOffset, region.TryLength, offset)) &&
            (!Holds(region.HandlerOffset, region.HandlerLength, blockStart) || Holds(region.HandlerOffset, region.HandlerLength, offset)));

    private static bool Holds(int start, int length, int offset) => offset >= start && offset < start + length;
}
