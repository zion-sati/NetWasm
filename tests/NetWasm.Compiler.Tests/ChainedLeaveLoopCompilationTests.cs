// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Svg.Custom 5.1.1's SvgDocument.CopyCompatibilityStyleStateTo stopped Devolutions' Avalonia sample with "Invalid
// structured control flow": two using blocks around a while (true) loop that returns from inside and breaks out to code
// inside both blocks. The leave chainer sends the return through a leave at the end of the inner try, which is also
// where the loop's break goes, and the loop's header is the inner try's entry, so the structurer took the loop for one
// around the try and gave the try's continuation the loop's break instead of its code. A goto past an inner finally to
// code inside the outer one made the same shape before the chainer. The traces record every finally and Dispose in
// order, and the loops around a try (a break, a return, a catch retrying) keep their labels. The checksum is the one
// desktop .NET 11 prints, in Release and Debug.
public sealed class ChainedLeaveLoopCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        public sealed class Trace
        {
            public long Value;
            public void Add(int step) => Value = Value * 10 + step;
        }

        public sealed class Items(Trace trace, int mark, params int[] items) : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() => new Enumerator(this);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(Items owner) : IEnumerator<int>
            {
                private int _index = -1;
                public int Current => owner._items[_index];
                object IEnumerator.Current => Current;
                public bool MoveNext() => ++_index < owner._items.Length;
                public void Reset() => _index = -1;
                public void Dispose() => owner._trace.Add(owner._mark);
            }

            private readonly Trace _trace = trace;
            private readonly int _mark = mark;
            private readonly int[] _items = items;
        }

        public static class Shapes
        {
            // Svg.Custom's CopyCompatibilityStyleStateTo: a return from the loop and a break out of it, inside two
            // using blocks.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int CopyState(Trace trace, Items source, Items target, Dictionary<int, int> state)
            {
                var copied = 0;
                using IEnumerator<int> first = source.GetEnumerator();
                using IEnumerator<int> second = target.GetEnumerator();
                while (true)
                {
                    var hasFirst = first.MoveNext();
                    var hasSecond = second.MoveNext();
                    if (hasFirst != hasSecond)
                    {
                        trace.Add(1);
                        return -1;
                    }
                    if (!hasFirst) break;
                    if (state.TryGetValue(first.Current, out var value)) copied += value * second.Current;
                }
                trace.Add(2);
                return copied;
            }

            // The same leaves written as a goto past the inner finally to code inside the outer one.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int GotoAfter(Trace trace, int start)
            {
                var count = start;
                try
                {
                    try
                    {
                        while (true)
                        {
                            if (count > 5)
                            {
                                trace.Add(1);
                                goto Out;
                            }
                            if (count++ > 2) break;
                            trace.Add(3);
                        }
                        trace.Add(2);
                    }
                    finally
                    {
                        trace.Add(4);
                    }
                Out:
                    trace.Add(5);
                }
                finally
                {
                    trace.Add(6);
                }
                return count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int ThreeDeep(Trace trace, int start, int stop)
            {
                var count = start;
                try
                {
                    try
                    {
                        try
                        {
                            while (true)
                            {
                                if (count == stop)
                                {
                                    trace.Add(1);
                                    return count;
                                }
                                if (count > 4) break;
                                count++;
                            }
                            trace.Add(2);
                        }
                        finally
                        {
                            trace.Add(3);
                        }
                        trace.Add(7);
                    }
                    finally
                    {
                        trace.Add(4);
                    }
                }
                finally
                {
                    trace.Add(5);
                }
                return -count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Caught(Trace trace, int start, int stop)
            {
                var count = start;
                try
                {
                    try
                    {
                        while (true)
                        {
                            if (count == stop)
                            {
                                trace.Add(1);
                                return 10 + count;
                            }
                            if (count > 4) break;
                            if (count == 3 && stop < 0) throw new InvalidOperationException();
                            count++;
                        }
                        trace.Add(2);
                    }
                    catch (InvalidOperationException)
                    {
                        trace.Add(9);
                    }
                }
                finally
                {
                    trace.Add(5);
                }
                return 20 + count;
            }

            // A loop whose body starts with the try: its break and return still leave the loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int LoopAroundTry(Trace trace, int stopAt, int returnAt)
            {
                var round = 0;
                while (true)
                {
                    try
                    {
                        round++;
                        if (round == stopAt) break;
                        if (round == returnAt) return round;
                        trace.Add(1);
                    }
                    finally
                    {
                        trace.Add(2);
                    }
                }
                trace.Add(3);
                return -round;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Risky(Trace trace, int attempt, int failures)
            {
                if (attempt <= failures) throw new InvalidOperationException();
                trace.Add(6);
                return attempt * 100;
            }

            // The retry loop: the catch falls back to the loop's head, which is the try's entry.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Retry(Trace trace, int failures)
            {
                var attempt = 0;
                while (true)
                {
                    try
                    {
                        attempt++;
                        return Risky(trace, attempt, failures);
                    }
                    catch (InvalidOperationException)
                    {
                        trace.Add(4);
                    }
                }
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                long sum = 17;
                void Mix(Trace trace, int result)
                {
                    sum = unchecked(sum * 1_000_003 + trace.Value);
                    sum = unchecked(sum * 1_000_003 + result);
                }
                var state = new Dictionary<int, int> { [1] = 2, [2] = 5 };
                for (var variant = 0; variant < 3; variant++)
                {
                    var trace = new Trace();
                    var source = new Items(trace, 7, 1, 2, input);
                    var target = variant == 1 ? new Items(trace, 8, 10, 20) : new Items(trace, 8, 10, 20, 30 + variant);
                    Mix(trace, Shapes.CopyState(trace, source, target, state));
                }
                for (var start = 0; start < 8; start += input - 1)
                {
                    var trace = new Trace();
                    Mix(trace, Shapes.GotoAfter(trace, start));
                }
                foreach (var stop in new[] { input, 9, -1 })
                {
                    var trace = new Trace();
                    Mix(trace, Shapes.ThreeDeep(trace, 1, stop));
                    trace = new Trace();
                    Mix(trace, Shapes.Caught(trace, 1, stop));
                }
                foreach (var (stopAt, returnAt) in new[] { (input, 9), (9, input), (1, 2) })
                {
                    var trace = new Trace();
                    Mix(trace, Shapes.LoopAroundTry(trace, stopAt, returnAt));
                }
                foreach (var failures in new[] { 0, input })
                {
                    var trace = new Trace();
                    Mix(trace, Shapes.Retry(trace, failures));
                }
                return (int)(unchecked((ulong)sum) % 1_000_000_007UL);
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm64)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void LeavesFromALoopInsideNestedProtectedBlocksRunAsOnDesktop(
        FinalWasmOptimization optimization,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"ChainedLeaveLoop_{optimization}_{target}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Equal(
            29238817,
            ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target, result.StaticDataEnd));
    }
}
