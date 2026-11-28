// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Merging;

namespace NetWasm.Compiler.ComponentModel.Tests.Merging;

// A merged section's buffer doubles, in 64 bits, up to the largest array: doubled in 32 bits a capacity past 1 GiB
// overflowed and every later write copied the whole buffer (over twenty minutes merging the three-app repro).
public sealed class WasmMergeSinkGrowthTests
{
    [Theory]
    [InlineData(16, 20L, 32)]
    [InlineData(16, 100L, 100)]
    [InlineData(1 << 30, (1L << 30) + 10, int.MaxValue)]
    [InlineData((1 << 30) + 64, (1L << 30) + 128, int.MaxValue)]
    public void TheBufferDoublesUpToTheLargestArray(int capacity, long needed, int expected)
    {
        Assert.Equal(Math.Min(expected, Array.MaxLength), WasmMergeSink.Grown(capacity, needed));
    }

    [Fact]
    public void ASectionLargerThanAnArrayIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => WasmMergeSink.Grown(1 << 30, (long)Array.MaxLength + 1));
    }

    [Fact]
    public void WritesGrowTheBufferAndKeepEveryByte()
    {
        var sink = new WasmMergeSink(16);
        for (var index = 0; index < 1000; index++) sink.Byte((byte)index);

        Assert.Equal(1000, sink.Length);
        Assert.Equal(Enumerable.Range(0, 1000).Select(index => (byte)index), sink.Written.ToArray());
    }
}
