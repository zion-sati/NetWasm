namespace NetWasm.Compiler.Tests.Correctness;

// Vector128<T> and Vector<T> values are Wasm v128 values. Each case moves one
// through a shape the compiler handles apart from a plain local: an
// address-taken local or argument, a ref parameter, an instance call, a field
// of a class, struct or static, an array element, a box, a generic argument, a
// closure, an async state machine, an iterator, an exception region and filter,
// default values, reinterpretation and the vectors' own fields. The results
// match desktop.
[Collection(CorrectnessTestGroup.Name)]
public sealed class VectorValueShapesCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void VectorValuesMatchDesktopInEveryStorageShape() =>
        runner.Run(new(
            "VectorValueShapesCorpus",
            "NetWasm.Correctness.VectorValueShapes",
            """
            using System;
            using System.Collections.Generic;
            using System.Numerics;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Runtime.Intrinsics;
            using System.Threading.Tasks;

            namespace NetWasm.Correctness.VectorValueShapes;

            public struct Holder
            {
                public int Tag;
                public Vector128<int> Value;
                public Vector128<float> Scale;
            }

            public sealed class Box
            {
                public Vector128<int> Value;
                public Vector<int> Wide;
            }

            public static class EntryPoint
            {
                private static Vector128<int> s_shared = Vector128.Create(5, 6, 7, 8);

                public static int Run(int input) => input switch
                {
                    0 => AddressTaken(input),
                    1 => ArgumentAddressTaken(Vector128.Create(1, 2, 3, 4)),
                    2 => RefParameters(),
                    3 => InstanceCalls(),
                    4 => Fields(),
                    5 => Arrays(),
                    6 => Boxing(),
                    7 => Generics(),
                    8 => Closures(),
                    9 => Async().GetAwaiter().GetResult(),
                    10 => Iterator(),
                    11 => Exceptions(input),
                    12 => Defaults(),
                    13 => Reinterpret(),
                    14 => Halves(),
                    15 => NumericsVector(),
                    _ => Spans(),
                };

                private static int Sum(Vector128<int> value) => Vector128.Sum(value);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Bump(ref Vector128<int> value) => value += Vector128<int>.One;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int AddressTaken(int input)
                {
                    var value = Vector128.Create(input, 10, 20, 30);
                    Bump(ref value);
                    ref var element = ref Unsafe.As<Vector128<int>, int>(ref value);
                    element = 100;
                    return Sum(value) * 31 + value[2];
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ArgumentAddressTaken(Vector128<int> value)
                {
                    Bump(ref value);
                    Bump(ref value);
                    return Sum(value);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Swap(ref Vector128<int> left, ref Vector128<int> right) => (left, right) = (right, left);

                private static int RefParameters()
                {
                    var left = Vector128.Create(1);
                    var right = Vector128.Create(2, 3, 4, 5);
                    Swap(ref left, ref right);
                    return Sum(left) * 100 + Sum(right);
                }

                private static int InstanceCalls()
                {
                    var value = Vector128.Create(3, 1, 4, 1);
                    var other = Vector128.Create(3, 1, 4, 1);
                    object boxed = other;
                    return (value.Equals(other) ? 1 : 0) + (value.Equals(boxed) ? 2 : 0) +
                        (value.GetHashCode() == other.GetHashCode() ? 4 : 0) + value.ToString().Length * 8 +
                        value[3] * 1000;
                }

                private static int Fields()
                {
                    var holder = new Holder { Tag = 7, Value = Vector128.Create(1, 2, 3, 4), Scale = Vector128.Create(0.5f) };
                    holder.Value *= 3;
                    var box = new Box { Value = holder.Value, Wide = new Vector<int>(2) };
                    box.Value += Vector128<int>.One;
                    s_shared = Vector128.Max(s_shared, box.Value);
                    var copy = holder;
                    copy.Value = Vector128<int>.Zero;
                    return Sum(holder.Value) + Sum(box.Value) * 10 + Sum(s_shared) * 100 + Sum(copy.Value) +
                        (int)Vector128.Sum(holder.Scale) * 1000 + Vector.Sum(box.Wide) * 10000 + holder.Tag;
                }

                private static int Arrays()
                {
                    var values = new Vector128<int>[5];
                    for (var i = 0; i < values.Length; i++) values[i] = Vector128.Create(i, i * 2, i * 3, i * 4);
                    ref var middle = ref values[2];
                    middle = Vector128.Negate(middle);
                    var total = Vector128<int>.Zero;
                    foreach (var value in values) total += value;
                    var copy = (Vector128<int>[])values.Clone();
                    Array.Reverse(copy);
                    return Sum(total) * 10 + Sum(copy[0]) + copy.Length;
                }

                private static int Boxing()
                {
                    object boxed = Vector128.Create(9, 8, 7, 6);
                    var back = (Vector128<int>)boxed;
                    ValueType asValueType = back + Vector128<int>.One;
                    var again = (Vector128<int>)asValueType;
                    object wide = new Vector<int>(3);
                    return Sum(back) * 100 + Sum(again) + (boxed is Vector128<int> ? 1000 : 0) + Vector.Sum((Vector<int>)wide);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Echo<T>(T value) => value;

                private static int Generics()
                {
                    var list = new List<Vector128<int>> { Vector128.Create(1), Vector128.Create(2) };
                    list.Add(Echo(Vector128.Create(3, 4, 5, 6)));
                    var map = new Dictionary<int, Vector128<int>> { [1] = list[2] };
                    var tuple = (Vector128.Create(7), 8);
                    return Sum(list[0]) + Sum(list[1]) * 10 + Sum(map[1]) * 100 + Sum(tuple.Item1) * 1000 + tuple.Item2;
                }

                private static int Closures()
                {
                    var captured = Vector128.Create(2, 4, 6, 8);
                    Func<Vector128<int>, Vector128<int>> add = value => value + captured;
                    captured *= 2;
                    return Sum(add(Vector128<int>.One));
                }

                // The vector lives across awaits, so it is a field of the state
                // machine; awaiting completed tasks keeps the run synchronous.
                private static async Task<int> Async()
                {
                    var value = Vector128.Create(1, 2, 3, 4);
                    await Task.CompletedTask;
                    value *= 5;
                    await new ValueTask<int>(2);
                    return Sum(value);
                }

                private static IEnumerable<Vector128<int>> Sequence(Vector128<int> start)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        yield return start;
                        start += Vector128<int>.One;
                    }
                }

                private static int Iterator()
                {
                    var total = 0;
                    foreach (var value in Sequence(Vector128.Create(10, 20, 30, 40))) total = total * 7 + Sum(value);
                    return total;
                }

                private static int Exceptions(int input)
                {
                    var value = Vector128.Create(1, 2, 3, input);
                    try
                    {
                        value *= 2;
                        if (value[0] == 2) throw new InvalidOperationException("x");
                        return -1;
                    }
                    catch (InvalidOperationException) when (value[1] == 4)
                    {
                        value += Vector128.Create(100);
                    }
                    finally
                    {
                        value += Vector128<int>.One;
                    }
                    return Sum(value);
                }

                private static int Defaults()
                {
                    Vector128<int> local = default;
                    var holder = new Holder();
                    var array = new Vector128<int>[2];
                    return (local == Vector128<int>.Zero ? 1 : 0) + (holder.Value == default ? 2 : 0) +
                        (array[1] == Vector128<int>.Zero ? 4 : 0) + (default(Vector<int>) == Vector<int>.Zero ? 8 : 0);
                }

                private static int Reinterpret()
                {
                    var four = new Vector4(1.5f, -2f, 3.25f, 8f);
                    var asVector = four.AsVector128();
                    var back = (asVector * 2f).AsVector4();
                    var bits = Unsafe.BitCast<Vector128<float>, Vector128<int>>(asVector);
                    var bytes = Unsafe.As<Vector128<float>, Vector128<byte>>(ref asVector);
                    return (int)(back.X + back.Y + back.Z + back.W) + bits.GetElement(1) % 1000 + bytes.GetElement(15);
                }

                private static int Halves()
                {
                    var value = Vector128.Create(1L, 2L);
                    var lower = value.GetLower();
                    var upper = value.GetUpper();
                    var swapped = Vector128.Create(upper, lower);
                    var replaced = value.WithUpper(Vector64.Create(40L));
                    return (int)(lower.ToScalar() * 1000 + upper.ToScalar() * 100 + swapped.GetElement(0) * 10 + replaced.GetElement(1));
                }

                private static int NumericsVector()
                {
                    var value = new Vector<int>(3);
                    var other = Vector<int>.One * 4;
                    var result = value * other + Vector<int>.One;
                    return result[0] * 1000 + Vector.Sum(result) / Vector<int>.Count + (result == new Vector<int>(13) ? 1 : 0);
                }

                public static int Trace() => 0;

                private static int Spans()
                {
                    Span<int> data = stackalloc int[12];
                    for (var i = 0; i < data.Length; i++) data[i] = i * i;
                    var vectors = MemoryMarshal.Cast<int, Vector128<int>>(data);
                    var total = Vector128<int>.Zero;
                    foreach (var vector in vectors) total += vector;
                    vectors[1] = total;
                    return Sum(total) * 100 + data[5];
                }
            }
            """,
            [.. Enumerable.Range(0, 17)])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
