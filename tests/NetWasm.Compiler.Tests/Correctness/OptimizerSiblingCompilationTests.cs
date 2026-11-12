using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Siblings of closed optimizer and generic issues, at every optimization preset: #148 (a static
// constructor's effects removed with an unused allocation), #153 (O3-only failures in interface
// writes and type names), #159 (a load hoisted past a write through a ref, a narrowing cast
// dropped through Min/Max, unsigned vector compares), and #63 and #66 (generic shapes reaching
// layouts they should not), #166 (a / b and a % b sharing one division) and #206 (null checks
// skipped on objects just allocated). Each program runs as desktop .NET does.
public sealed class OptimizerSiblingCompilationTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string, FinalWasmOptimization> Cases
    {
        get
        {
            var data = new TheoryData<string, FinalWasmOptimization>();
            foreach (var name in Programs.Keys)
            {
                foreach (var optimization in new[]
                {
                    FinalWasmOptimization.None, FinalWasmOptimization.O1, FinalWasmOptimization.O2,
                    FinalWasmOptimization.O3, FinalWasmOptimization.Os, FinalWasmOptimization.Oz,
                })
                {
                    data.Add(name, optimization);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ASiblingRunsAsDesktopDoesAtEveryPreset(string name, FinalWasmOptimization optimization) =>
        runner.Run(new("Optimizer" + name + optimization, "OptimizerSiblings", Prelude + Programs[name], [0, 3, 7])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
            Optimization = optimization,
            ExecuteOptimizedWasm = optimization is not (FinalWasmOptimization.None or FinalWasmOptimization.O0),
        });

    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Runtime.Intrinsics;
        namespace OptimizerSiblings;

        public static partial class EntryPoint
        {
            public static int Trace() => 0;
        }

        """;

    private static readonly Dictionary<string, string> Programs = new()
    {
        ["StaticConstructorEffects"] = """
            public static class Log { public static int Value; }
            public class Precise { static Precise() { Log.Value += 1; } public Precise() { } }
            public class Lazy2 { public static readonly int Field = Compute(); static int Compute() { Log.Value += 10; return 5; } }
            public static class Holder<T> { public static readonly string Name; static Holder() { Log.Value += 100; Name = typeof(T).Name; } }
            public struct Valued { static Valued() { Log.Value += 1000; } public int X; public static int Touch() => 1; }
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Log.Value = 0;
                    _ = new Precise();
                    var before = Log.Value;
                    var field = input > 100 ? Lazy2.Field : 0;
                    _ = Holder<int>.Name;
                    _ = Holder<string>.Name;
                    _ = Holder<int>.Name;
                    var valued = default(Valued);
                    var touched = Valued.Touch();
                    return before * 100000 + Log.Value + field + valued.X + touched;
                }
            }
            """,
        ["AliasedWrites"] = """
            public struct Pair { public int A; public int B; }
            public static partial class EntryPoint
            {
                private static int ThroughRef(int[] values, ref int alias)
                {
                    var total = 0;
                    for (var i = 0; i < values.Length; i++)
                    {
                        total += values[0];
                        alias = i + 1;
                    }
                    return total;
                }

                private static unsafe float Reinterpret(int bits)
                {
                    var value = bits;
                    ref var asFloat = ref Unsafe.As<int, float>(ref value);
                    asFloat = 1.5f;
                    return BitConverter.Int32BitsToSingle(value) + asFloat;
                }

                public static unsafe int Run(int input)
                {
                    var values = new int[] { input, 2, 3 };
                    var viaRef = ThroughRef(values, ref values[0]);
                    var span = values.AsSpan();
                    var sum = 0;
                    for (var i = 0; i < span.Length; i++) { sum += values[1]; span[1] = i; }
                    var pair = new Pair { A = input };
                    ref var a = ref pair.A;
                    var spans = MemoryMarshal.CreateSpan(ref pair.A, 2);
                    spans[0] = 9;
                    var pairRead = a + pair.B;
                    var buffer = stackalloc int[4];
                    var pointer = buffer;
                    for (var i = 0; i < 4; i++) { pointer[i] = i; buffer[0] += pointer[i]; }
                    object[] covariant = new string[] { "a", "b" };
                    var stored = 0;
                    try { covariant[0] = 5; } catch (ArrayTypeMismatchException) { stored = 7; }
                    return viaRef * 10000 + sum * 100 + pairRead * 10 + buffer[0] + stored + (int)Reinterpret(input);
                }
            }
            """,
        ["NarrowingThroughIntrinsics"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    sbyte small = (sbyte)(input * 40);
                    byte unsignedSmall = (byte)(input * 70);
                    short half = (short)(input * 9000);
                    char letter = (char)(input * 20000);
                    var results = new long[]
                    {
                        Math.Min((sbyte)(small * 3), (sbyte)50), Math.Max((byte)(unsignedSmall + 200), (byte)10),
                        Math.Abs((short)(half * 4)), Math.Clamp((sbyte)(small + 100), (sbyte)-5, (sbyte)5),
                        Math.Sign((short)(half - 30000)), (ushort)Math.Max(letter, (char)65),
                        BitOperations.PopCount((uint)(sbyte)small), BitOperations.LeadingZeroCount((byte)unsignedSmall),
                        BitOperations.RotateLeft((uint)(short)half, 3), (sbyte)Math.Min((int)small, 300),
                        unchecked((byte)Math.Max(unsignedSmall * 2, 0)), Math.Min((ulong)(uint)input, ulong.MaxValue - 1) > 5 ? 1 : 0,
                    };
                    var hash = 17L;
                    foreach (var value in results) hash = unchecked(hash * 31 + value);
                    return (int)(hash ^ (hash >> 32));
                }
            }
            """,
        ["UnsignedAndVectorCompares"] = """
            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    var big = ulong.MaxValue - (ulong)input;
                    var small = (ulong)input;
                    var left = Vector128.Create(big, small);
                    var right = Vector128.Create(small, big);
                    var greater = Vector128.GreaterThan(left, right);
                    var less = Vector128.LessThan(left.AsUInt32(), right.AsUInt32());
                    var signed = Vector128.GreaterThan(left.AsInt64(), right.AsInt64());
                    var min = Vector128.Min(left.AsUInt16(), right.AsUInt16());
                    return (greater.GetElement(0) != 0 ? 1 : 0) + (greater.GetElement(1) != 0 ? 10 : 0) +
                        (less.GetElement(0) != 0 ? 100 : 0) + (signed.GetElement(0) != 0 ? 1000 : 0) +
                        min.GetElement(0) % 97 * 10000 + (big > small ? 1000000 : 0);
                }
            }
            """,
        // Types compared by identity: a program carries no type names (issue 225).
        ["InterfaceWritesAndTypeIdentity"] = """
            public class Outer { public class Inner<T> { } }
            public static partial class EntryPoint
            {
                private static Type TypeOf<T>() => typeof(T);

                public static int Run(int input)
                {
                    IList<int> list = new List<int> { 1, 2, 3 };
                    list[1] = input;
                    ICollection<int> collection = list;
                    collection.Add(4);
                    IReadOnlyList<int> readOnly = (List<int>)list;
                    IList<string> array = new string[2];
                    array[0] = "x";
                    object[] objects = { new List<int>(), new int[0, 0], new Outer.Inner<string>(), 5L, "s", (int?)3 };
                    Type[] expected = { typeof(List<int>), typeof(int[,]), typeof(Outer.Inner<string>), typeof(long), typeof(string), typeof(int) };
                    var matches = 0;
                    for (var index = 0; index < objects.Length; index++)
                        if (objects[index].GetType() == expected[index]) matches += 1 << index;
                    if (TypeOf<Dictionary<int, string>>() == typeof(Dictionary<int, string>)) matches += 64;
                    if (TypeOf<int[]>() == typeof(int[]) && typeof(Outer.Inner<int>) != typeof(Outer.Inner<string>)) matches += 128;
                    if (typeof(Outer.Inner<>) != typeof(Outer.Inner<int>) && typeof(Outer) != typeof(Outer.Inner<int>)) matches += 256;
                    return list[1] * 1000 + readOnly.Count * 100 + array[0]!.Length * 10 + matches;
                }
            }
            """,
        ["GenericShapes"] = """
            public abstract class Node<TSelf> where TSelf : Node<TSelf>, new()
            {
                public static int Created;
                public static TSelf Make() { Created++; return new TSelf(); }
                public abstract int Depth();
            }
            public sealed class Leaf : Node<Leaf> { public override int Depth() => 1; }
            public class Container<T>
            {
                public static List<T> Shared = new();
                public virtual TResult Map<TResult>(T value, Func<T, TResult> map) => map(value);
            }
            public sealed class Doubling<T> : Container<T>
            {
                public override TResult Map<TResult>(T value, Func<T, TResult> map) => map(map(value) is T again ? again : value);
            }
            public readonly struct Wrapper<T> : IEquatable<Wrapper<T>> where T : IEquatable<T>
            {
                public readonly T Value;
                public Wrapper(T value) => Value = value;
                public bool Equals(Wrapper<T> other) => Value.Equals(other.Value);
            }
            public static partial class EntryPoint
            {
                private static bool Same<T>(T left, T right) where T : IEquatable<T> => left.Equals(right);

                public static int Run(int input)
                {
                    var leaf = Node<Leaf>.Make();
                    Container<int>.Shared.Add(input);
                    Container<string>.Shared.Add("s");
                    Container<int> container = new Doubling<int>();
                    var mapped = container.Map<int>(input, value => value + 1);
                    var text = container.Map<string>(input, value => value.ToString());
                    Func<Func<int, int>, int> apply = map => map(input);
                    var same = Same(new Wrapper<int>(input), new Wrapper<int>(input)) && !Same(new Wrapper<string>("a"), new Wrapper<string>("b"));
                    return leaf.Depth() * 100000 + Node<Leaf>.Created * 10000 + Container<int>.Shared.Count * 1000 + mapped * 10 +
                        text.Length + apply(value => value * 3) * 100 + (same ? 1 : 0);
                }
            }
            """,
        ["DivisionAndRemainderPairs"] = """
            public static partial class EntryPoint
            {
                private static int s_calls;

                private static int Next(int value) { s_calls++; return value; }

                private static long Pair(long left, long right) => left / right * 1000 + left % right;

                public static int Run(int input)
                {
                    var outcomes = new List<long>();
                    int[] lefts = [input * 7 + 3, -input * 5 - 1, int.MinValue, int.MaxValue, 0];
                    int[] rights = [input + 2, -3, -1, 1, input - 3];
                    foreach (var left in lefts)
                    {
                        foreach (var right in rights)
                        {
                            try { outcomes.Add(left / right); outcomes.Add(left % right); }
                            catch (DivideByZeroException) { outcomes.Add(-7); }
                            catch (OverflowException) { outcomes.Add(-8); }
                            try { outcomes.Add(Pair(left, right)); } catch (DivideByZeroException) { outcomes.Add(-9); }
                            if (right != 0) outcomes.Add((uint)left / (uint)right + (uint)left % (uint)right);
                            var (quotient, remainder) = right is 0 or -1 ? (0, 0) : Math.DivRem(left, right);
                            outcomes.Add(quotient * 31 + remainder);
                        }
                    }
                    var divisor = input + 1;
                    var shared = Next(100) / divisor + Next(100) % divisor;
                    var hash = 17L;
                    foreach (var outcome in outcomes) hash = unchecked(hash * 31 + outcome);
                    return (int)(hash ^ (hash >> 32)) + shared * 7 + s_calls;
                }
            }
            """,
        ["NewObjectShapes"] = """
            public class Base
            {
                protected readonly int Seed;
                public Base() { Seed = Describe().Length; }
                public virtual string Describe() => "base";
            }
            public sealed class Derived : Base
            {
                private readonly string _name = "derived";
                public Derived() { }
                public override string Describe() => _name ?? "uninitialized";
                public int SeedSeen => Seed;
            }
            public class Throwing { public Throwing(int value) { if (value > 2) throw new ArgumentOutOfRangeException(nameof(value)); } public int Value = 1; }
            public struct Point { public int X; public Point(int x) => X = x; }
            public static partial class EntryPoint
            {
                private static Derived? s_last;

                public static unsafe int Run(int input)
                {
                    var derived = new Derived();
                    s_last = derived;
                    Throwing? made = null;
                    var thrown = 0;
                    try { made = new Throwing(input); } catch (ArgumentOutOfRangeException) { thrown = 1; }
                    var empty = new string((char*)null);
                    var fromArray = new string(new char[0]);
                    var point = new Point(input);
                    object boxed = new Point(input + 1);
                    var array = new int[input];
                    return derived.SeedSeen * 100000 + (made?.Value ?? 9) * 10000 + thrown * 1000 + empty.Length + fromArray.Length +
                        point.X * 10 + ((Point)boxed).X * 100 + array.Length + (s_last == derived ? 1 : 0);
                }
            }
            """,
    };
}
