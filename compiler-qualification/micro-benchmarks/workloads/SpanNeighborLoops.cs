// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace NetWasm.Workloads
{
    // Counted loops that read a span at an offset from the index, as an image filter reads a
    // pixel's neighbors in its row and in the row above: `start + index - 1`,
    // `start + index - width`. Every index stays in range for the whole loop, which one test
    // before the loop can prove. Rows of a 640x480 image, and one loop that reads a span at the
    // index itself under a count.
    public class SpanNeighborLoops
    {
        private const int Width = 640;
        private const int Height = 480;

        private readonly int[] _source = new int[Width * Height];
        private readonly int[] _destination = new int[Width * Height];
        private readonly int[] _table = new int[256];

        public SpanNeighborLoops()
        {
            for (var index = 0; index < _source.Length; index++) _source[index] = index * 3 + 1;
            for (var index = 0; index < _table.Length; index++) _table[index] = index * 7 + 3;
        }

        // Each pixel adds the pixel above it: nothing carries from one iteration to the next.
        [Benchmark]
        public int AddAbove()
        {
            for (var row = 1; row < Height; row++) AddAbove(_destination, row * Width, Width, Width);
            return _destination[^1];
        }

        // A three-tap average of one span into another.
        [Benchmark]
        public int Stencil()
        {
            for (var row = 0; row < Height; row++) Stencil(_source, _destination, row * Width + 1, Width - 2);
            return _destination[^2];
        }

        // Left plus above minus above-left, of one span into another.
        [Benchmark]
        public int Gradient()
        {
            for (var row = 1; row < Height; row++) Gradient(_source, _destination, row * Width + 1, Width - 1, Width);
            return _destination[^1];
        }

        // A table lookup of each of the first count values: the loop's bound is neither span's length.
        [Benchmark]
        public int LookupByCount() => LookupByCount(_source, _table, _source.Length);

        // Each pixel adds the average of its left neighbor, just written, and the pixel above:
        // every iteration waits for the one before it.
        [Benchmark]
        public int PredictAverage()
        {
            for (var row = 1; row < Height; row++) PredictAverage(_destination, row * Width + 1, Width - 1, Width);
            return _destination[^1];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddAbove(Span<int> pixels, int start, int count, int width)
        {
            for (var index = 0; index < count; index++)
            {
                var at = start + index;
                pixels[at] += pixels[at - width] | 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Stencil(ReadOnlySpan<int> source, Span<int> destination, int start, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var at = start + index;
                destination[at] = (source[at - 1] + source[at] + source[at + 1]) / 3;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Gradient(ReadOnlySpan<int> source, Span<int> destination, int start, int count, int width)
        {
            for (var index = 0; index < count; index++)
            {
                var at = start + index;
                destination[at] = source[at - 1] + source[at - width] - source[at - width - 1];
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int LookupByCount(ReadOnlySpan<int> values, ReadOnlySpan<int> table, int count)
        {
            var sum = 0;
            for (var index = 0; index < count; index++) sum ^= table[values[index] & 255] + index;
            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PredictAverage(Span<int> pixels, int start, int count, int width)
        {
            for (var index = 0; index < count; index++)
            {
                var at = start + index;
                pixels[at] += (pixels[at - 1] + pixels[at - width]) >> 1;
            }
        }
    }
}
