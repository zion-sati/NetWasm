using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Field reads that take an earlier read's value and stores that both arms of
// an if merge into one, against desktop: two names for one object, calls and
// refs that write the field between reads, a static initializer that runs
// between two static reads, loops whose iterations write the field, SciMark's
// random generator, and merged stores whose value throws or whose object is
// null.
public sealed class FieldReuseCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void FieldReuseMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "FieldReuseCorpus",
            "NetWasm.Correctness.FieldReuse",
            Source,
            [.. Enumerable.Range(0, 6)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            #nullable disable
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.FieldReuse;

            public sealed class Node
            {
                public int X, Y;
                public Node Next;
                public static int Count;
            }

            // An explicit initializer runs exactly at the first access.
            public static class Late
            {
                public static int Value;

                static Late() => Value = Init();

                private static int Init()
                {
                    Node.Count += 100;
                    return 5;
                }
            }

            public sealed class Generator
            {
                private readonly int[] _m = new int[17];
                private int _i = 4, _j = 16;
                private readonly int _m1 = int.MaxValue;
                private readonly double _dm1 = 1.0 / int.MaxValue;

                public Generator(int seed)
                {
                    for (var index = 0; index < 17; index++) _m[index] = seed = unchecked(seed * 69069 + 1) & int.MaxValue;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                public double Next()
                {
                    var k = _m[_i] - _m[_j];
                    if (k < 0) k += _m1;
                    _m[_j] = k;
                    if (_i == 0) _i = 16; else _i--;
                    if (_j == 0) _j = 16; else _j--;
                    return _dm1 * k;
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var hash = 17L;
                    for (var round = 0; round < 6; round++)
                    {
                        var value = input switch
                        {
                            0 => Aliased(new Node { X = round, Y = 2 }, round),
                            1 => Written(new Node { X = round + 3 }),
                            2 => Statics(round),
                            3 => Loop(new Node { X = 1, Next = new Node { X = 2 } }, round),
                            4 => Random(round),
                            5 => Merged(round % 2 == 0 ? new Node { X = 4 } : null, round),
                            _ => throw new ArgumentOutOfRangeException(nameof(input)),
                        };
                        hash = unchecked(hash * 31 + value);
                    }
                    return unchecked((int)(hash ^ (hash >> 32)));
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Aliased(Node node, int round)
                {
                    var other = round % 2 == 0 ? node : new Node { X = 9 };
                    var first = node.X;
                    other.X = first + 10;
                    var second = node.X;
                    other.Y = 7;
                    return first * 1000 + second * 10 + node.X + node.Y;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Written(Node node)
                {
                    var first = node.X;
                    Bump(node);
                    var second = node.X;
                    Add(ref node.Y, second);
                    var third = node.Y + node.Y;
                    Replace(ref node);
                    return first + second * 10 + third * 100 + node.X * 1000;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Bump(Node node) => node.X += 5;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Add(ref int value, int amount) => value += amount;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Replace(ref Node node) => node = new Node { X = 8 };

                // Late's initializer writes Node.Count when Late.Value is first read.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Statics(int round)
                {
                    var before = Node.Count;
                    var late = Late.Value;
                    var after = Node.Count;
                    Node.Count = before + round;
                    return before + late * 10 + after * 100 + Node.Count * 1000;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Loop(Node node, int round)
                {
                    var sum = 0L;
                    var start = node.X;
                    for (var index = 0; index < round + 2; index++)
                    {
                        sum += node.X * 3 + node.Next.X;
                        if (index % 2 == 1) node.X += index;
                        node.Next.X = node.X;
                    }
                    return sum * 100 + start + node.X;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Random(int round)
                {
                    var generator = new Generator(113 + round);
                    var under = 0;
                    for (var count = 0; count < 4000; count++)
                    {
                        var x = generator.Next();
                        var y = generator.Next();
                        if (x * x + y * y <= 1.0) under++;
                    }
                    return under;
                }

                // A null object throws after the value is computed, as desktop does.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Merged(Node node, int round)
                {
                    var effects = 0;
                    try
                    {
                        if (round < 3) node.X = Divide(12, round); else node.X = round * 2;
                        if (round % 3 == 0) node.Y = node.X + 1; else node.Y = node.X - 1;
                        return node.X * 100 + node.Y;
                    }
                    catch (NullReferenceException) { effects = 1; }
                    catch (DivideByZeroException) { effects = 2; }
                    return -effects;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Divide(int a, int b) => a / b;

                public static int Trace() => 0;
            }
            """;
}
