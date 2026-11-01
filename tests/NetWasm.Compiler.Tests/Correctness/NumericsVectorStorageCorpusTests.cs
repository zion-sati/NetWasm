namespace NetWasm.Compiler.Tests.Correctness;

// Vector2, Vector4, Quaternion and Plane are v128 values, Vector2 in the low 8
// bytes. Each case stores them where a value type can live (fields, statics,
// arrays, spans, boxes, generics, closures and views of another width) and
// passes structs by value to callees that change their copy, and compares the
// results with desktop .NET.
public sealed class NumericsVectorStorageCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NumericsVectorStorageMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "NumericsVectorStorageCorpus",
            "NetWasm.Correctness.NumericsVectorStorage",
            Source,
            [0, 1, 2, 3, 4, 5, 6])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Collections.Generic;
            using System.Numerics;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Runtime.Intrinsics;

            namespace NetWasm.Correctness.NumericsVectorStorage;

            public struct Packed
            {
                public float Before;
                public Vector4 Wide;
                public Vector2 Narrow;
                public byte After;
                public Quaternion Rotation;
            }

            public sealed class Holder
            {
                public Vector2 Narrow;
                public Vector4 Wide;
                public Quaternion Rotation;
                public Plane Surface;
            }

            public interface IShape
            {
                Vector4 Scale(Vector4 value, Vector2 factor);
            }

            public class Shape : IShape
            {
                public virtual Vector4 Scale(Vector4 value, Vector2 factor) => value * factor.X + new Vector4(factor.Y);
            }

            public sealed class Stretched : Shape
            {
                public override Vector4 Scale(Vector4 value, Vector2 factor) => base.Scale(value, factor) * 2;
            }

            public static class EntryPoint
            {
                private static Vector4 s_wide = new(1, 2, 3, 4);
                private static Vector2 s_narrow = new(5, 6);

                public static int Run(int input) => input switch
                {
                    0 => Fields(),
                    1 => Arrays(),
                    2 => BoxesAndGenerics(),
                    3 => StaticsAndCalls(),
                    4 => Views(),
                    5 => Arguments(),
                    _ => Elements(),
                };

                private static unsafe int Fields()
                {
                    var h = 17;
                    var packed = new Packed { Before = 1, Wide = new(2, 3, 4, 5), Narrow = new(6, 7), After = 8, Rotation = new(9, 10, 11, 12) };
                    h = Mix(h, Unsafe.SizeOf<Packed>());
                    h = Mix(h, (int)Unsafe.ByteOffset(ref Unsafe.As<float, byte>(ref packed.Before), ref Unsafe.As<Vector4, byte>(ref packed.Wide)));
                    h = Mix(h, (int)Unsafe.ByteOffset(ref Unsafe.As<float, byte>(ref packed.Before), ref Unsafe.As<Vector2, byte>(ref packed.Narrow)));
                    h = Mix(h, (int)Unsafe.ByteOffset(ref Unsafe.As<float, byte>(ref packed.Before), ref packed.After));
                    h = Mix(h, (int)Unsafe.ByteOffset(ref Unsafe.As<float, byte>(ref packed.Before), ref Unsafe.As<Quaternion, byte>(ref packed.Rotation)));
                    packed.Narrow.Y = 70;
                    packed.Wide.Z += 40;
                    packed.Rotation.W = -12;
                    h = Mix(h, packed.Before); h = Mix(h, packed.Wide); h = Mix(h, packed.Narrow); h = Mix(h, packed.After); h = Mix(h, packed.Rotation);
                    var holder = new Holder { Narrow = new(1, 2), Wide = new(3, 4, 5, 6), Rotation = Quaternion.Identity, Surface = new(1, 2, 3, 4) };
                    holder.Narrow *= 3;
                    holder.Wide.W = holder.Narrow.X;
                    holder.Surface.D = 9;
                    holder.Surface.Normal = Vector3.Normalize(holder.Surface.Normal);
                    h = Mix(h, holder.Narrow); h = Mix(h, holder.Wide); h = Mix(h, holder.Rotation); h = Mix(h, holder.Surface);
                    var surface = holder.Surface;
                    var normal = surface.Normal;
                    h = Mix(h, normal); h = Mix(h, surface.D);
                    return h;
                }

                private static int Arrays()
                {
                    var h = 19;
                    var narrow = new Vector2[5];
                    for (var i = 0; i < narrow.Length; i++) narrow[i] = new Vector2(i, -i);
                    narrow[2].X = 20;
                    foreach (var value in MemoryMarshal.Cast<Vector2, float>(narrow)) h = Mix(h, value);
                    var wide = new Vector4[3];
                    wide[1] = new Vector4(narrow[3], 7, 8);
                    wide[2] = wide[1] * wide[1];
                    foreach (var value in wide) h = Mix(h, value);
                    Span<Vector2> stack = stackalloc Vector2[4];
                    for (var i = 0; i < stack.Length; i++) stack[i] = narrow[i] + narrow[i + 1];
                    foreach (var value in stack) h = Mix(h, value);
                    ref var middle = ref stack[1];
                    middle.Y = 99;
                    h = Mix(h, stack[1]); h = Mix(h, stack[2]);
                    var planes = new[] { new Plane(1, 0, 0, 2), new Plane(0, 1, 0, -3) };
                    h = Mix(h, planes[1].Normal); h = Mix(h, planes[0].D);
                    var rotations = new List<Quaternion> { Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f) };
                    rotations.Add(rotations[0] * rotations[0]);
                    foreach (var value in rotations) h = Mix(h, value);
                    return h;
                }

                private static int BoxesAndGenerics()
                {
                    var h = 23;
                    var narrow = new Vector2(1.5f, -2);
                    object boxed = narrow;
                    h = Mix(h, (Vector2)boxed); h = Mix(h, boxed.Equals(narrow) ? 1 : 0); h = Mix(h, boxed.Equals(new Vector2(1.5f, 2)) ? 1 : 0);
                    object wide = new Vector4(narrow, 3, 4);
                    h = Mix(h, wide is Vector4 unboxed ? unboxed : default);
                    var counts = new Dictionary<Vector2, int>();
                    for (var i = 0; i < 20; i++)
                    {
                        var key = new Vector2(i % 5, i % 3);
                        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                    }
                    h = Mix(h, counts.Count); h = Mix(h, counts[new Vector2(1, 1)]);
                    h = Mix(h, EqualityComparer<Vector4>.Default.Equals(new Vector4(1), Vector4.One) ? 1 : 0);
                    h = Mix(h, Same(Quaternion.Identity, new Quaternion(0, 0, 0, 1)) ? 1 : 0);
                    h = Mix(h, Pass(new Plane(1, 2, 3, 4))); h = Mix(h, Pass(narrow)); h = Mix(h, Pass(Vector4.UnitW));
                    var pair = (Narrow: narrow, Wide: Vector4.One);
                    pair.Narrow.X = 8;
                    h = Mix(h, pair.Narrow); h = Mix(h, pair.Wide);
                    h = Mix(h, narrow.GetHashCode() == new Vector2(1.5f, -2).GetHashCode() ? 1 : 0);
                    return Mix(h, narrow.ToString().Length);
                }

                private static int StaticsAndCalls()
                {
                    var h = 29;
                    s_wide.Y = 20;
                    s_narrow += Vector2.One;
                    h = Mix(h, s_wide); h = Mix(h, s_narrow);
                    var captured = new Vector2(3, 4);
                    Func<Vector2, Vector2> add = value => captured += value;
                    h = Mix(h, add(Vector2.One)); h = Mix(h, add(Vector2.UnitY)); h = Mix(h, captured);
                    IShape shape = new Stretched();
                    h = Mix(h, shape.Scale(new Vector4(1, 2, 3, 4), new Vector2(2, 0.5f)));
                    h = Mix(h, new Shape().Scale(Vector4.One, Vector2.One));
                    h = Mix(h, Recurse(new Vector2(1, 1), 6));
                    return h;
                }

                private static int Views()
                {
                    var h = 31;
                    var wide = new Vector4(1, 2, 3, 4);
                    Unsafe.As<Vector4, Vector2>(ref wide) = new Vector2(9, 8);
                    h = Mix(h, wide);
                    var low = Unsafe.As<Vector4, Vector2>(ref wide);
                    h = Mix(h, low);
                    var narrow = new Vector2(5, 6);
                    ref var x = ref narrow.X;
                    x *= 7;
                    h = Mix(h, narrow);
                    var widened = narrow.AsVector128();
                    h = Mix(h, widened.GetElement(0)); h = Mix(h, widened.GetElement(2)); h = Mix(h, widened.GetElement(3));
                    h = Mix(h, narrow.AsVector128Unsafe().AsVector2());
                    var three = new Vector3(1, 2, 3);
                    var lifted = three.AsVector128();
                    h = Mix(h, lifted.GetElement(2)); h = Mix(h, lifted.GetElement(3)); h = Mix(h, lifted.AsVector3());
                    h = Mix(h, Vector4.Create(three, 9)); h = Mix(h, Vector4.Create(narrow, 1, 2)); h = Mix(h, Vector3.Create(narrow, 4));
                    var quaternion = new Quaternion(1, 2, 3, 4).AsVector128().AsQuaternion();
                    h = Mix(h, quaternion); h = Mix(h, new Plane(1, 2, 3, 4).AsVector128().AsPlane());
                    var plane = new Plane(5, 6, 7, 8);
                    h = Mix(h, plane.Normal); h = Mix(h, plane.D); h = Mix(h, plane.Normal.Y);
                    return h;
                }

                private static int Arguments()
                {
                    var h = 37;
                    var matrix = new Matrix3x2(1, 2, 3, 4, 5, 6);
                    h = Mix(h, Mutate(matrix)); h = Mix(h, matrix);
                    h = Mix(h, MutateFirst(matrix, matrix)); h = Mix(h, matrix);
                    var other = Matrix3x2.Identity;
                    h = Mix(h, First(matrix, matrix = other)); h = Mix(h, matrix);
                    var large = Matrix4x4.CreateScale(2);
                    h = Mix(h, Mutate(large)); h = Mix(h, large.M11);
                    var pair = new KeyValuePair<string, int>("seven", 7);
                    h = Mix(h, Measure(pair, pair)); h = Mix(h, pair.Key.Length + pair.Value);
                    var digits = new[] { 1, 2, 3, 4 };
                    Span<int> span = digits;
                    h = Mix(h, Shrink(span)); h = Mix(h, span.Length);
                    ReadOnlySpan<char> text = "abcdef";
                    h = Mix(h, Count(text, text.Slice(2)));
                    var memory = new Memory<byte>(new byte[] { 1, 2, 3, 4, 5 }).Slice(1);
                    h = Mix(h, Sum(memory)); h = Mix(h, memory.Length);
                    return h;
                }

                private static int Elements()
                {
                    var h = 41;
                    var vector = Vector128.Create(1, 2, 3, 4);
                    for (var index = 0; index < 4; index++) h = Mix(h, vector.WithElement(index, 10 * index).GetElement(index));
                    h = Mix(h, Vector128.Create(1L, 2L).WithElement(1, -5L).GetElement(1));
                    h = Mix(h, Vector128.Create((byte)1).WithElement(15, (byte)200).GetElement(15));
                    h = Mix(h, Vector128.Create(1.5).WithElement(0, -2.5).GetElement(0));
                    try
                    {
                        h = Mix(h, vector.WithElement(Sixteen() / 4, 1).GetElement(0));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        h = Mix(h, 77);
                    }
                    return h;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Sixteen() => 16;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float Mutate(Matrix3x2 value)
                {
                    value.M11 = 99;
                    value.Translation = new Vector2(-1, -2);
                    return value.M11 + value.M32;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float Mutate(Matrix4x4 value)
                {
                    value.M11 = 50;
                    return value.M11 + value.M22;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float MutateFirst(Matrix3x2 first, Matrix3x2 second)
                {
                    first.M12 = 40;
                    return first.M12 + second.M12;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float First(Matrix3x2 first, Matrix3x2 second) => first.M11 * 10 + second.M11;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Measure(KeyValuePair<string, int> first, KeyValuePair<string, int> second)
                {
                    first = new KeyValuePair<string, int>("x", 1);
                    return first.Key.Length + first.Value + second.Key.Length + second.Value;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Shrink(Span<int> values)
                {
                    values[0] = 9;
                    values = values.Slice(1);
                    return values.Length + values[0];
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Count(ReadOnlySpan<char> text, ReadOnlySpan<char> tail) => text.Length * 10 + tail.Length;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Sum(Memory<byte> memory)
                {
                    var total = 0;
                    foreach (var value in memory.Span) total += value;
                    memory = memory.Slice(1);
                    return total * 10 + memory.Length;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static Vector2 Recurse(Vector2 value, int depth) =>
                    depth == 0 ? value : Recurse(value * 1.5f + Vector2.UnitX, depth - 1) - value;

                private static bool Same<T>(T left, T right) where T : IEquatable<T> => left.Equals(right);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Pass<T>(T value) => value;

                private static int Mix(int hash, float value) => unchecked(hash * 31 + BitConverter.SingleToInt32Bits(value));
                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);
                private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value ^ (int)(value >> 32));
                private static int Mix(int hash, double value) => Mix(hash, BitConverter.DoubleToInt64Bits(value));
                private static int Mix(int hash, Vector2 value) => Mix(Mix(hash, value.X), value.Y);
                private static int Mix(int hash, Vector3 value) => Mix(Mix(Mix(hash, value.X), value.Y), value.Z);
                private static int Mix(int hash, Vector4 value) => Mix(Mix(Mix(Mix(hash, value.X), value.Y), value.Z), value.W);
                private static int Mix(int hash, Quaternion value) => Mix(Mix(Mix(Mix(hash, value.X), value.Y), value.Z), value.W);
                private static int Mix(int hash, Plane value) => Mix(Mix(hash, value.Normal), value.D);
                private static int Mix(int hash, Matrix3x2 m) => Mix(Mix(Mix(Mix(Mix(Mix(hash, m.M11), m.M12), m.M21), m.M22), m.M31), m.M32);

                public static int Trace() => 0;
            }
            """;
}
