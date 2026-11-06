namespace NetWasm.Compiler.Tests.Correctness;

// Vector3 is a v128 value whose fourth lane is undefined, and 12 bytes in
// memory. Each case moves it through every place a value type can live (struct
// and class fields at every offset, statics, arrays, spans, boxes, nullables,
// generics, closures and tuples), through calls by value, by reference and as
// receivers, through views of other widths and Plane's Normal, and through the
// operations that read only three lanes, and compares the results with desktop
// .NET.
public sealed class NumericsVector3StorageCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NumericsVector3StorageMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "NumericsVector3StorageCorpus",
            "NetWasm.Correctness.NumericsVector3Storage",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7])
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

            namespace NetWasm.Correctness.NumericsVector3Storage;

            public struct Particle
            {
                public byte Tag;
                public Vector3 Position;
                public float Mass;
                public Vector3 Velocity;
                public short Flags;
            }

            public struct Wrapped : IEquatable<Wrapped>
            {
                public Vector3 Value;
                public int Count;

                public bool Equals(Wrapped other) => Value == other.Value && Count == other.Count;
                public override bool Equals(object? obj) => obj is Wrapped other && Equals(other);
                public override int GetHashCode() => HashCode.Combine(Value, Count);
            }

            public struct Plain
            {
                public Vector3 Value;
                public int Count;
            }

            public sealed class Body
            {
                public Vector3 Position;
                public float Mass;
                public Vector3 Velocity;
                public Vector3[] Trail = new Vector3[3];

                public Vector3 Momentum => Velocity * Mass;
                public void Step(float time) => Position += Velocity * time;
            }

            public interface IField
            {
                Vector3 Sample(Vector3 at, float scale);
            }

            public class Gravity : IField
            {
                public virtual Vector3 Sample(Vector3 at, float scale) => -Vector3.UnitY * scale + at * 0.25f;
            }

            public sealed class Drag : Gravity
            {
                public override Vector3 Sample(Vector3 at, float scale) => base.Sample(at, scale) * 0.5f - at;
            }

            public static class EntryPoint
            {
                private static Vector3 s_origin = new(1, 2, 3);
                private static readonly Vector3 s_delta = new(0.5f, -0.5f, 0.25f);
                private static Vector3[] s_points = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };

                public static int Run(int input) => input switch
                {
                    0 => Fields(),
                    1 => Arrays(),
                    2 => BoxesAndGenerics(),
                    3 => StaticsAndCalls(),
                    4 => Views(),
                    5 => References(),
                    6 => Operations(),
                    _ => Elements(),
                };

                private static int Fields()
                {
                    var h = 17;
                    var particle = new Particle { Tag = 1, Position = new(2, 3, 4), Mass = 5, Velocity = new(6, 7, 8), Flags = 9 };
                    h = Mix(h, Unsafe.SizeOf<Particle>());
                    h = Mix(h, (int)Unsafe.ByteOffset(ref particle.Tag, ref Unsafe.As<Vector3, byte>(ref particle.Position)));
                    h = Mix(h, (int)Unsafe.ByteOffset(ref particle.Tag, ref Unsafe.As<float, byte>(ref particle.Mass)));
                    h = Mix(h, (int)Unsafe.ByteOffset(ref particle.Tag, ref Unsafe.As<Vector3, byte>(ref particle.Velocity)));
                    h = Mix(h, (int)Unsafe.ByteOffset(ref particle.Tag, ref Unsafe.As<short, byte>(ref particle.Flags)));
                    particle.Position.Y = 30;
                    particle.Velocity += particle.Position;
                    h = Mix(h, particle.Tag); h = Mix(h, particle.Position); h = Mix(h, particle.Mass); h = Mix(h, particle.Velocity); h = Mix(h, particle.Flags);
                    var body = new Body { Position = new(1, 1, 1), Mass = 2, Velocity = new(0.5f, 1, -1) };
                    body.Step(2);
                    body.Trail[1] = body.Position;
                    body.Trail[2].Z = 7;
                    h = Mix(h, body.Position); h = Mix(h, body.Momentum); h = Mix(h, body.Trail[1]); h = Mix(h, body.Trail[2]);
                    var copy = particle;
                    copy.Position = Vector3.Zero;
                    h = Mix(h, copy.Position); h = Mix(h, particle.Position);
                    return h;
                }

                private static int Arrays()
                {
                    var h = 19;
                    var points = new Vector3[7];
                    for (var i = 0; i < points.Length; i++) points[i] = new Vector3(i, -i, i * 0.5f);
                    points[2].X = 20;
                    points[3] += points[2];
                    foreach (var value in MemoryMarshal.Cast<Vector3, float>(points)) h = Mix(h, value);
                    var sum = Vector3.Zero;
                    foreach (var point in points) sum += point;
                    h = Mix(h, sum);
                    Span<Vector3> stack = stackalloc Vector3[5];
                    for (var i = 0; i < stack.Length; i++) stack[i] = points[i] + points[i + 1];
                    ref var middle = ref stack[2];
                    middle.Z = 99;
                    foreach (var value in stack) h = Mix(h, value);
                    var grid = new Vector3[2, 3];
                    grid[1, 2] = new Vector3(4, 5, 6);
                    grid[0, 1] = grid[1, 2] * 2;
                    h = Mix(h, grid[0, 1]); h = Mix(h, grid[1, 2]); h = Mix(h, grid[1, 0]);
                    var jagged = new[] { new[] { Vector3.One }, new[] { Vector3.UnitZ, Vector3.UnitX } };
                    h = Mix(h, jagged[1][1]);
                    var particles = new Particle[4];
                    for (var i = 0; i < particles.Length; i++) particles[i].Velocity = new Vector3(i, 1, 2);
                    for (var i = 0; i < particles.Length; i++) particles[i].Position += particles[i].Velocity * 0.5f;
                    foreach (var particle in particles) { h = Mix(h, particle.Position); h = Mix(h, particle.Flags); }
                    Array.Reverse(points);
                    h = Mix(h, points[0]); h = Mix(h, points.AsSpan(1, 2).ToArray()[1]);
                    var copied = new Vector3[3];
                    Array.Copy(points, 2, copied, 0, 3);
                    h = Mix(h, copied[2]);
                    return h;
                }

                private static int BoxesAndGenerics()
                {
                    var h = 23;
                    var value = new Vector3(1.5f, -2, 3);
                    object boxed = value;
                    h = Mix(h, (Vector3)boxed); h = Mix(h, boxed.Equals(value) ? 1 : 0); h = Mix(h, boxed.Equals(new Vector3(1.5f, -2, -3)) ? 1 : 0);
                    h = Mix(h, boxed is Vector3 unboxed ? unboxed : default);
                    Vector3? maybe = value;
                    object boxedMaybe = maybe;
                    h = Mix(h, (Vector3)boxedMaybe); h = Mix(h, maybe.GetValueOrDefault()); h = Mix(h, maybe.HasValue ? 1 : 0);
                    maybe = null;
                    h = Mix(h, maybe.GetValueOrDefault(Vector3.One));
                    var counts = new Dictionary<Vector3, int>();
                    for (var i = 0; i < 30; i++)
                    {
                        var key = new Vector3(i % 5, i % 3, i % 2);
                        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                    }
                    h = Mix(h, counts.Count); h = Mix(h, counts[new Vector3(1, 1, 1)]);
                    var list = new List<Vector3> { value, Vector3.One };
                    list.Add(list[0] * list[1]);
                    list.Sort((left, right) => left.X.CompareTo(right.X));
                    foreach (var item in list) h = Mix(h, item);
                    var lengths = 0f;
                    list.ForEach(item => lengths += item.Length());
                    h = Mix(h, lengths);
                    h = Mix(h, EqualityComparer<Vector3>.Default.Equals(new Vector3(1), Vector3.One) ? 1 : 0);
                    h = Mix(h, Same(value, new Vector3(1.5f, -2, 3)) ? 1 : 0);
                    h = Mix(h, Pass(value)); h = Mix(h, Pass(new Wrapped { Value = value, Count = 4 }).Value);
                    var wrapped = new Wrapped { Value = value, Count = 2 };
                    h = Mix(h, wrapped.Equals(new Wrapped { Value = value, Count = 2 }) ? 1 : 0);
                    h = Mix(h, wrapped.GetHashCode() == new Wrapped { Value = value, Count = 2 }.GetHashCode() ? 1 : 0);
                    object plain = new Plain { Value = value, Count = 3 };
                    h = Mix(h, plain.Equals(new Plain { Value = value, Count = 3 }) ? 1 : 0);
                    h = Mix(h, plain.Equals(new Plain { Value = -value, Count = 3 }) ? 1 : 0);
                    h = Mix(h, plain.GetHashCode() == new Plain { Value = value, Count = 3 }.GetHashCode() ? 1 : 0);
                    var pair = (First: value, Second: Vector3.UnitY);
                    pair.First.X = 8;
                    h = Mix(h, pair.First); h = Mix(h, pair.Second);
                    h = Mix(h, value.GetHashCode() == new Vector3(1.5f, -2, 3).GetHashCode() ? 1 : 0);
                    return Mix(h, value.ToString().Length);
                }

                private static int StaticsAndCalls()
                {
                    var h = 29;
                    s_origin.Y = 20;
                    s_origin += s_delta;
                    s_points[1] = s_origin;
                    h = Mix(h, s_origin); h = Mix(h, s_delta); h = Mix(h, s_points[1]);
                    var captured = new Vector3(3, 4, 5);
                    Func<Vector3, Vector3> add = value => captured += value;
                    h = Mix(h, add(Vector3.One)); h = Mix(h, add(Vector3.UnitZ)); h = Mix(h, captured);
                    IField field = new Drag();
                    h = Mix(h, field.Sample(new Vector3(1, 2, 3), 2));
                    h = Mix(h, new Gravity().Sample(Vector3.One, 1));
                    h = Mix(h, Recurse(new Vector3(1, 1, 1), 6));
                    h = Mix(h, Mutate(s_origin)); h = Mix(h, s_origin);
                    h = Mix(h, Sum3(Vector3.One, Vector3.UnitX, s_delta, 2));
                    return h;
                }

                private static int Views()
                {
                    var h = 31;
                    var three = new Vector3(1, 2, 3);
                    var lifted = three.AsVector128();
                    h = Mix(h, lifted.GetElement(0)); h = Mix(h, lifted.GetElement(2)); h = Mix(h, lifted.GetElement(3));
                    h = Mix(h, lifted.AsVector3()); h = Mix(h, three.AsVector128Unsafe().AsVector3());
                    h = Mix(h, (three.AsVector128Unsafe() * 2).AsVector3());
                    h = Mix(h, Vector4.Create(three, 9)); h = Mix(h, new Vector4(three, 8));
                    h = Mix(h, Vector3.Create(new Vector2(4, 5), 6)); h = Mix(h, new Vector3(new Vector2(7, 8), 9));
                    h = Mix(h, new Vector4(1, 2, 3, 4).AsVector128().AsVector3());
                    h = Mix(h, Unsafe.BitCast<Vector4, Vector128<float>>(new Vector4(three, 5)).GetElement(3));
                    try
                    {
                        h = Mix(h, Unsafe.BitCast<Vector3, Vector128<float>>(three).GetElement(0));
                    }
                    catch (NotSupportedException)
                    {
                        h = Mix(h, 55);
                    }
                    try
                    {
                        h = Mix(h, Unsafe.BitCast<Vector2, Vector128<float>>(new Vector2(4, 5)).GetElement(0));
                    }
                    catch (NotSupportedException)
                    {
                        h = Mix(h, 56);
                    }
                    var plane = new Plane(5, 6, 7, 8);
                    h = Mix(h, plane.Normal); h = Mix(h, plane.D); h = Mix(h, plane.Normal.Y);
                    plane.Normal = new Vector3(-1, -2, -3);
                    h = Mix(h, plane); h = Mix(h, Plane.Normalize(plane));
                    var wide = new Vector4(1, 2, 3, 4);
                    Unsafe.As<Vector4, Vector3>(ref wide) = new Vector3(9, 8, 7);
                    h = Mix(h, wide); h = Mix(h, Unsafe.As<Vector4, Vector3>(ref wide));
                    var bytes = new byte[16];
                    MemoryMarshal.Write(bytes.AsSpan(2), new Vector3(1, -1, 2));
                    h = Mix(h, MemoryMarshal.Read<Vector3>(bytes.AsSpan(2))); h = Mix(h, bytes[14]); h = Mix(h, bytes[15]);
                    var quaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
                    h = Mix(h, Vector3.Transform(three, quaternion));
                    h = Mix(h, Vector3.Transform(three, Matrix4x4.CreateRotationZ(0.25f)));
                    h = Mix(h, Vector3.TransformNormal(three, Matrix4x4.CreateScale(2)));
                    return h;
                }

                private static int References()
                {
                    var h = 37;
                    var value = new Vector3(1, 2, 3);
                    Scale(ref value, 3);
                    h = Mix(h, value);
                    Produce(out var produced);
                    h = Mix(h, produced);
                    h = Mix(h, Read(in value));
                    ref var y = ref value.Y;
                    y *= 7;
                    h = Mix(h, value);
                    var length = value.Length();
                    var normal = Vector3.Normalize(value);
                    h = Mix(h, length); h = Mix(h, normal);
                    var particle = new Particle { Position = value };
                    Scale(ref particle.Position, 0.5f);
                    h = Mix(h, particle.Position);
                    var body = new Body { Velocity = value };
                    Scale(ref body.Velocity, -1);
                    h = Mix(h, body.Velocity);
                    Scale(ref s_points[2], 4);
                    h = Mix(h, s_points[2]);
                    var span = new Vector3[] { value, normal }.AsSpan();
                    Scale(ref span[1], 10);
                    h = Mix(h, span[1]);
                    return h;
                }

                private static int Operations()
                {
                    var h = 41;
                    var a = new Vector3(1, -2, 3);
                    var b = new Vector3(-4, 5, 0.5f);
                    h = Mix(h, a + b); h = Mix(h, a - b); h = Mix(h, a * b); h = Mix(h, a / b); h = Mix(h, -a); h = Mix(h, a * 2); h = Mix(h, 2 * a); h = Mix(h, a / 4);
                    h = Mix(h, Vector3.Dot(a, b)); h = Mix(h, Vector3.Cross(a, b)); h = Mix(h, Vector3.Distance(a, b)); h = Mix(h, Vector3.DistanceSquared(a, b));
                    h = Mix(h, a.Length()); h = Mix(h, a.LengthSquared()); h = Mix(h, Vector3.Normalize(a));
                    h = Mix(h, Vector3.Min(a, b)); h = Mix(h, Vector3.Max(a, b)); h = Mix(h, Vector3.Clamp(a, b, b * 2)); h = Mix(h, Vector3.Abs(a)); h = Mix(h, Vector3.SquareRoot(Vector3.Abs(b)));
                    h = Mix(h, Vector3.Lerp(a, b, 0.25f));
                    h = Mix(h, a == b ? 1 : 0); h = Mix(h, a != b ? 1 : 0); h = Mix(h, a == new Vector3(1, -2, 3) ? 1 : 0); h = Mix(h, a.Equals(b) ? 1 : 0);
                    h = Mix(h, Vector3.Equals(a, a)); h = Mix(h, Vector3.GreaterThanAll(a, b) ? 1 : 0); h = Mix(h, Vector3.LessThanAny(a, b) ? 1 : 0);
                    h = Mix(h, Vector3.Sum(a)); h = Mix(h, Vector3.Shuffle(a, 2, 0, 1));
                    var nan = new Vector3(float.NaN, 1, 2);
                    var same = nan;
                    h = Mix(h, nan == same ? 1 : 0); h = Mix(h, nan.Equals(same) ? 1 : 0);
                    var total = Vector3.Zero;
                    for (var i = 0; i < 100; i++) total = total * 0.9f + new Vector3(i, i * 0.5f, -i);
                    h = Mix(h, total);
                    return h;
                }

                private static int Elements()
                {
                    var h = 43;
                    var value = new Vector3(1, 2, 3);
                    for (var index = 0; index < 3; index++) h = Mix(h, value[index]);
                    value[1] = 20;
                    h = Mix(h, value);
                    h = Mix(h, value.WithElement(2, 30)); h = Mix(h, value.GetElement(0));
                    var copy = new float[5];
                    value.CopyTo(copy, 1);
                    foreach (var item in copy) h = Mix(h, item);
                    h = Mix(h, new Vector3(copy.AsSpan(1)));
                    try
                    {
                        h = Mix(h, value[Three()]);
                    }
                    catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException)
                    {
                        h = Mix(h, error is IndexOutOfRangeException ? 77 : 78);
                    }
                    return h;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Three() => 3;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float Mutate(Vector3 value)
                {
                    value.X = 99;
                    value += Vector3.One;
                    return value.X + value.Z;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static Vector3 Sum3(Vector3 first, Vector3 second, Vector3 third, float scale) =>
                    (first + second + third) * scale;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Scale(ref Vector3 value, float scale) => value *= scale;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Produce(out Vector3 value) => value = new Vector3(-1, 0.5f, 6);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static float Read(in Vector3 value) => value.X + value.Y * 10 + value.Z * 100;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static Vector3 Recurse(Vector3 value, int depth) =>
                    depth == 0 ? value : Recurse(value * 1.5f + Vector3.UnitX, depth - 1) - value;

                private static bool Same<T>(T left, T right) where T : IEquatable<T> => left.Equals(right);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Pass<T>(T value) => value;

                private static int Mix(int hash, float value) => unchecked(hash * 31 + BitConverter.SingleToInt32Bits(value));
                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);
                private static int Mix(int hash, Vector3 value) => Mix(Mix(Mix(hash, value.X), value.Y), value.Z);
                private static int Mix(int hash, Vector4 value) => Mix(Mix(Mix(Mix(hash, value.X), value.Y), value.Z), value.W);
                private static int Mix(int hash, Plane value) => Mix(Mix(hash, value.Normal), value.D);

                public static int Trace() => 0;
            }
            """;
}
