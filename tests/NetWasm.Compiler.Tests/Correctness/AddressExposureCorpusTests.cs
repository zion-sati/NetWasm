using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A local or argument whose address is taken anywhere in a method can change
// wherever that address can reach: through a ref, a span or a pointer taken
// earlier, or through a call that writes where an earlier pointer to it was
// kept. Each shape the optimizations rewrite still sees every such change: in
// loops, hoisting calls, reducing, vectorizing, dropping bounds checks,
// forwarding stores, unrolling, collapsing, folding, hoisting fields and
// recognizing fills; and in straight-line code, specializing constant
// arguments, folding dereferences, reading struct and vector fields, reusing
// field loads and pure calls, eliminating span locals, devirtualizing exact
// receivers and replacing allocations.
public sealed class AddressExposureCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void OptimizationsSeeWritesThroughEarlierAddresses(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "AddressExposureCorpus",
            "NetWasm.Correctness.AddressExposure",
            Source,
            [.. Enumerable.Range(0, 30)])
        {
            Optimization = optimization,
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            #nullable disable
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.Intrinsics;

            namespace NetWasm.Correctness.AddressExposure;

            public struct Point
            {
                public int X;
                public int Y;
            }

            public sealed class Node
            {
                public int Value;
                public Node Next;
            }

            public sealed class Holder
            {
                public int F;
            }

            public abstract class Shape
            {
                public abstract int Sides();
            }

            public sealed class Triangle : Shape
            {
                public override int Sides() => 3;
            }

            public sealed class Square : Shape
            {
                public override int Sides() => 4;
            }

            public static unsafe class EntryPoint
            {
                private static double* s_double;
                private static int* s_int;

                public static int Run(int input)
                {
                    var n = 3 + (input & 1);
                    try
                    {
                        return input switch
                        {
                            0 => Bits(HoistThroughRef(n)),
                            1 => Bits(HoistThroughSpan(n)),
                            2 => Bits(HoistThroughPointer(n)),
                            3 => Bits(HoistThroughStashedPointer(n)),
                            4 => Bits(HoistArgument(1, n)),
                            5 => ReduceWithBoundThroughRef([5, 9, 2, 7, 1, 8, 3], 7),
                            6 => (int)SumWithAccumulatorThroughRef([1, 2, 3, 4, 5, 6, 7, 8], 8),
                            7 => ScaleWithFactorThroughRef([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], 3),
                            8 => IndexThroughRef([1, 2, 3, 4, 5]),
                            9 => ConstantTripCounterThroughRef(),
                            10 => FoldWithAddendThroughRef(n + 5, 2),
                            11 => FieldThroughRef(n + 4),
                            12 => FillWithValueThroughRef(new int[9], 4),
                            13 => CollapseWithValueThroughRef(n + 3, 7),
                            14 => WalkThroughRef(List(9)),
                            15 => ForwardThroughRef([4, 5, 6, 7], 4),
                            16 => FieldReadThroughSlotRef(new Holder { F = 2 }, new Holder { F = 50 }, n + 2),
                            17 => ReduceWithElementThroughPointer([3, 1, 4, 1, 5, 9, 2, 6], 8),
                            18 => UnrolledSumThroughSpan([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]),
                            19 => UnrolledSumThroughPointer([5, 4, 3, 2, 1, 6, 7, 8, 9]),
                            20 => ConstantArgumentThroughRef(3),
                            21 => ConstantArgumentThroughPointer(3),
                            22 => DereferenceThroughRef(n),
                            23 => StructFieldThroughRef(n),
                            24 => VectorElementThroughRef(n),
                            25 => SpanLocalThroughRef(n),
                            26 => ReceiverThroughRef(n),
                            27 => AllocationThroughRef(n),
                            28 => PureCallThroughRef(n),
                            29 => OutArgumentThroughRef(n),
                            _ => -1,
                        };
                    }
                    catch (IndexOutOfRangeException)
                    {
                        return -7;
                    }
                }

                private static int Bits(double value) => (int)(BitConverter.DoubleToInt64Bits(value) >> 20);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double HoistThroughRef(int n)
                {
                    double y = 1, z = 0;
                    ref double r = ref y;
                    for (var i = 0; i < n; i++) { z += Math.Sqrt(y); r += 1; }
                    return z;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double HoistThroughSpan(int n)
                {
                    double y = 1, z = 0;
                    var s = new Span<double>(ref y);
                    for (var i = 0; i < n; i++) { z += Math.Sqrt(y); s[0] += 1; }
                    return z;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double HoistThroughPointer(int n)
                {
                    double y = 1, z = 0;
                    double* p = &y;
                    for (var i = 0; i < n; i++) { z += Math.Sqrt(y); *p += 1; }
                    return z;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Bump() => *s_double += 1;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double HoistThroughStashedPointer(int n)
                {
                    double y = 1, z = 0;
                    s_double = &y;
                    for (var i = 0; i < n; i++) { z += Math.Sqrt(y); Bump(); }
                    s_double = null;
                    return z;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double HoistArgument(double y, int n)
                {
                    double z = 0;
                    ref double r = ref y;
                    for (var i = 0; i < n; i++) { z += Math.Cos(y); r += 0.5; }
                    return z;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReduceWithBoundThroughRef(int[] a, int n)
                {
                    var m = int.MinValue;
                    ref var bound = ref n;
                    for (var i = 0; i < n; i++) { m = Math.Max(m, a[i]); bound--; }
                    return m * 100 + n;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long SumWithAccumulatorThroughRef(int[] a, int n)
                {
                    long s = 0;
                    ref var total = ref s;
                    for (var i = 0; i < n; i++) { s += a[i]; total *= 3; }
                    return s % 1000003;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ScaleWithFactorThroughRef(int[] a, int k)
                {
                    var b = new int[a.Length];
                    ref var factor = ref k;
                    for (var i = 0; i < a.Length; i++) { b[i] = a[i] * k; factor++; }
                    var h = 0;
                    foreach (var value in b) h = h * 31 + value;
                    return h;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int IndexThroughRef(int[] a)
                {
                    var s = 0;
                    var i = 0;
                    ref var index = ref i;
                    for (; i < a.Length; i++) { index += 2; s += a[i]; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ConstantTripCounterThroughRef()
                {
                    var s = 0;
                    var i = 0;
                    ref var counter = ref i;
                    for (; i < 6; i++) { s = s * 10 + i; counter++; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int FoldWithAddendThroughRef(int n, int c)
                {
                    var acc = 0;
                    var p = &c;
                    for (var i = 0; i < n; i++) { acc += c; *p += 1; }
                    return acc;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int FieldThroughRef(int n)
                {
                    var point = new Point { X = 1, Y = 2 };
                    ref var x = ref point.X;
                    var s = 0;
                    for (var i = 0; i < n; i++) { s += point.X * 10 + point.Y; x++; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int FillWithValueThroughRef(int[] a, int v)
                {
                    ref var value = ref v;
                    for (var i = 0; i < a.Length; i++) { a[i] = v; value += 3; }
                    var h = 0;
                    foreach (var element in a) h = h * 7 + element;
                    return h;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int CollapseWithValueThroughRef(int n, int c)
                {
                    var x = 0;
                    s_int = &c;
                    for (var i = 0; i < n; i++) { x = c; *s_int += 5; }
                    s_int = null;
                    return x * 100 + c;
                }

                private static Node List(int count)
                {
                    Node head = null;
                    for (var i = count; i > 0; i--) head = new Node { Value = i, Next = head };
                    return head;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int WalkThroughRef(Node head)
                {
                    var s = 0;
                    var node = head;
                    ref var current = ref node;
                    for (; node != null; node = node.Next)
                    {
                        s = s * 3 + node.Value;
                        if (current.Next != null) current = current.Next;
                    }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ForwardThroughRef(int[] a, int n)
                {
                    var s = 0;
                    var x = 0;
                    ref var stored = ref x;
                    for (var i = 0; i < n; i++) { x = a[i]; stored = x * 10; s += x; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int FieldReadThroughSlotRef(Holder first, Holder second, int n)
                {
                    var holder = first;
                    ref var slot = ref holder;
                    var s = 0;
                    for (var i = 0; i < n; i++) { s += holder.F; slot = i % 2 == 0 ? second : first; s += holder.F; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReduceWithElementThroughPointer(int[] a, int n)
                {
                    fixed (int* p = a)
                    {
                        var m = 0;
                        for (var i = 0; i < n; i++) { m = Math.Max(m, a[i]); p[(i + 1) % n] += 10; }
                        return m;
                    }
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int UnrolledSumThroughSpan(int[] a)
                {
                    var s = 0;
                    var view = new Span<int>(ref s);
                    for (var i = 0; i < a.Length; i++) { s += a[i]; view[0] ^= i; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int UnrolledSumThroughPointer(int[] a)
                {
                    var s = 0;
                    var p = &s;
                    for (var i = 0; i < a.Length; i++) { s += a[i]; *p *= 3; }
                    return s;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ConstantArgumentThroughRef(int x)
                {
                    ref var r = ref x;
                    r += 5;
                    return x * 10 + r;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ConstantArgumentThroughPointer(int x)
                {
                    s_int = &x;
                    Increase();
                    s_int = null;
                    return x;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Increase() => *s_int += 40;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int DereferenceThroughRef(int n)
                {
                    var x = n;
                    var p = &x;
                    ref var r = ref *p;
                    r = 9;
                    *p += x;
                    return x + r;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int StructFieldThroughRef(int n)
                {
                    var point = new Point { X = n, Y = 2 };
                    var before = point.X;
                    ref var whole = ref point;
                    whole = new Point { X = 70, Y = 80 };
                    return before * 1000 + point.X + point.Y;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int VectorElementThroughRef(int n)
                {
                    var vector = Vector128.Create(n, 2, 3, 4);
                    var before = vector.GetElement(0);
                    ref var whole = ref vector;
                    whole = Vector128.Create(9);
                    return before * 100 + vector.GetElement(0) + vector.GetElement(3);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int SpanLocalThroughRef(int n)
                {
                    ReadOnlySpan<byte> data = [1, 2, 3, 4];
                    var before = data[n & 1];
                    ref var whole = ref data;
                    whole = new byte[] { 50, 60, 70, 80 };
                    return before * 1000 + data[0] + data.Length;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReceiverThroughRef(int n)
                {
                    Shape shape = new Triangle();
                    var before = shape.Sides();
                    ref var slot = ref shape;
                    if (n > 0) slot = new Square();
                    return before * 10 + shape.Sides();
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int AllocationThroughRef(int n)
                {
                    var holder = new Holder { F = n };
                    var before = holder.F;
                    ref var slot = ref holder;
                    slot = new Holder { F = 60 };
                    holder.F++;
                    return before * 100 + holder.F;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int PureCallThroughRef(int n)
                {
                    double x = n;
                    var first = Math.Sin(x);
                    ref var r = ref x;
                    r = 1.5;
                    var second = Math.Sin(x);
                    return (int)(first * 1000) * 10000 + (int)(second * 1000);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Fill(out int value, int with) => value = with;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int OutArgumentThroughRef(int n)
                {
                    var x = 0;
                    var before = x;
                    Fill(out x, n * 11);
                    return before * 1000 + x;
                }

                public static int Trace() => 0;
            }
            """;
}
