// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Loops that read a span at start + index run a copy without the per-element check when start >= 0 and
// start + bound <= length hold before them, and the loop as written otherwise: an index out of range throws at the
// iteration that reaches it, after the writes of the iterations before it, whatever the sign of start or the size of
// start + bound.
public sealed class ArrayLoopVersioningCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void CopiesRunOnlyWhereTheirChecksHold(WasmTarget target, bool enabled)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayLoopVersioning", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(
                LoopUnrolling: enabled ? ManagedLoopUnrolling.Enabled : ManagedLoopUnrolling.Disabled)));
        // The test before a copy widens the bound unsigned; nothing else in these methods does.
        foreach (var name in new[] { "Adler", "Gather", "Unsigned", "Predict", "Prefix" })
        {
            var method = compilation.Program.Methods.Values.Single(method => method.Method.Definition.Name == name);
            Assert.Equal(enabled, method.Body.Instructions.Any(instruction =>
                instruction.Operation == CilOperation.ConvertInt64Unsigned));
        }
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        public static class EntryPoint
        {
            public static int Run(int unused)
            {
                var data = new byte[12000];
                for (var i = 0; i < data.Length; i++) data[i] = (byte)(i * 7 + (i >> 5));
                if (Adler(data) != 275666154u) return 1;
                if (Adler(data.AsSpan(3, 9001)) != 3634659573u) return 2;
                if (Adler(ReadOnlySpan<byte>.Empty) != 1u) return 3;

                var input = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                var output = new int[8];
                if (Gather(output, input, 2, 5) != 3 + 4 + 5 + 6 + 7 || output[4] != 7 || output[5] != 0) return 4;
                if (Gather(output, input, 0, 8) != 36 || Gather(output, input, 8, 0) != 0) return 5;
                // Past the end: the loop as written copies three elements, then throws.
                Array.Clear(output);
                try { Gather(output, input, 5, 4); return 6; }
                catch (IndexOutOfRangeException) { if (output[0] != 6 || output[2] != 8 || output[3] != 0) return 7; }
                // A negative start throws before anything is written.
                Array.Clear(output);
                try { Gather(output, input, -1, 2); return 8; }
                catch (IndexOutOfRangeException) { if (output[0] != 0) return 9; }
                // start + count wraps in 32 bits.
                try { Gather(output, input, int.MaxValue - 1, 4); return 10; }
                catch (IndexOutOfRangeException) { }
                // An unsigned guard reads a negative bound as one past int.MaxValue.
                try { Unsigned(input, 0, -1); return 11; }
                catch (IndexOutOfRangeException) { }
                if (Unsigned(input, 1, 3) != 2 + 3 + 4 || Unsigned(input, 0, 0) != 0) return 12;

                // A predictor reads the left neighbor and the row above: rows of 8, the third row.
                var image = new int[40];
                for (var i = 0; i < image.Length; i++) image[i] = i * 3 + 1;
                var expected = (int[])image.Clone();
                ReferencePredict(expected, 16, 8, 8);
                Predict(image, 16, 8, 8);
                if (!image.AsSpan().SequenceEqual(expected)) return 13;
                // The first pixel has no left neighbor: index -1 throws before anything is written.
                var before = (int[])image.Clone();
                try { Predict(image, 0, 4, 8); return 14; }
                catch (IndexOutOfRangeException) { if (!image.AsSpan().SequenceEqual(before)) return 15; }
                // With width 0 the row above is the pixel itself, so only the left neighbor is out of range.
                try { Predict(image, 0, 3, 0); return 22; }
                catch (IndexOutOfRangeException) { if (!image.AsSpan().SequenceEqual(before)) return 23; }
                // The first row has no row above: index 3 - 8 throws before anything is written.
                try { Predict(image, 3, 2, 8); return 16; }
                catch (IndexOutOfRangeException) { if (!image.AsSpan().SequenceEqual(before)) return 17; }
                // Past the end: pixels 36 to 39 are written, then index 40 throws.
                ReferencePredict(expected, 36, 4, 8);
                try { Predict(image, 36, 6, 8); return 18; }
                catch (IndexOutOfRangeException) { if (!image.AsSpan().SequenceEqual(expected)) return 19; }
                // A negative width reads past the pixel: at 38 it reads index 40 and throws after eight writes.
                expected = (int[])image.Clone();
                ReferencePredict(expected, 30, 8, -2);
                try { Predict(image, 30, 9, -2); return 20; }
                catch (IndexOutOfRangeException) { if (!image.AsSpan().SequenceEqual(expected)) return 21; }

                // A loop bounded by a count reads and writes at the index itself.
                Array.Clear(output);
                if (Prefix(output, input, 8) != 36 || output[7] != 8) return 24;
                if (Prefix(output, input, 0) != 0 || Prefix(output, input, -3) != 0) return 25;
                // Past the input: its eight elements are copied, then index 8 throws.
                var wide = new int[12];
                try { Prefix(wide, input, 9); return 26; }
                catch (IndexOutOfRangeException) { if (wide[7] != 8 || wide[8] != 0) return 27; }
                // Past the output: three elements are written, then index 3 throws.
                var narrow = new int[3];
                try { Prefix(narrow, input, 5); return 28; }
                catch (IndexOutOfRangeException) { if (narrow[0] != 1 || narrow[2] != 3) return 29; }
                return 42;
            }

            // The count is the method's argument, and neither span's length.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Prefix(Span<int> output, ReadOnlySpan<int> input, int count)
            {
                var sum = 0;
                for (var index = 0; index < count; index++)
                {
                    var value = input[index];
                    output[index] = value;
                    sum += value;
                }
                return sum;
            }

            // A VP8L predictor's row: each pixel adds the average of its left neighbor and the pixel above, read at
            // start + index - 1 and start + index - width. Its start, count and width are the method's arguments.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Predict(Span<int> pixels, int start, int count, int width)
            {
                for (var index = 0; index < count; index++)
                {
                    var at = start + index;
                    pixels[at] += (pixels[at - 1] + pixels[at - width]) >> 1;
                }
            }

            // The same through an array's own checks, which the versioner leaves alone.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ReferencePredict(int[] pixels, int start, int count, int width)
            {
                for (var index = 0; index < count; index++)
                {
                    var at = start + index;
                    pixels[at] += (pixels[at - 1] + pixels[at - width]) >> 1;
                }
            }

            // Sdcb.SimdCodecs.Png's Adler-32: each chunk's bytes are read at offset + index.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static uint Adler(ReadOnlySpan<byte> data)
            {
                uint a = 1, b = 0;
                Update(ref a, ref b, data);
                return b << 16 | a;
            }

            private static void Update(ref uint a, ref uint b, ReadOnlySpan<byte> data)
            {
                int count;
                for (int offset = 0; offset < data.Length; offset += count)
                {
                    count = Math.Min(5552, data.Length - offset);
                    for (int index = 0; index < count; index++)
                    {
                        a += data[offset + index];
                        b += a;
                    }
                    a %= 65521u;
                    b %= 65521u;
                }
            }

            // Locals hold the start and count, as a chunked loop's offset and count are.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Gather(Span<int> output, ReadOnlySpan<int> input, int first, int length)
            {
                int start = first, count = length, sum = 0;
                for (var index = 0; index < count; index++)
                {
                    var value = input[start + index];
                    output[index] = value;
                    sum += value;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Unsigned(ReadOnlySpan<int> input, int first, int length)
            {
                int start = first, count = length, sum = 0;
                for (uint index = 0; index < (uint)count; index++) sum += input[start + (int)index];
                return sum;
            }
        }
        """;
}
