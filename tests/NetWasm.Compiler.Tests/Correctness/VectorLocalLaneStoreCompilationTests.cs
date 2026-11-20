// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A System.Numerics vector local or argument written through its address keeps desktop .NET's results once it is a
// register value: a lane store, a compound assignment through the lane's address or a dup of the variable's, an
// indexer, a ref local, an Unsafe.As view, a split struct's vector field and inlined ref and in parameters. The
// hazards the rewrite must leave alone run too: the variable stored or stored through between its address and the
// lane store, nested stores and a throw between them. A lane store whose value a call computes that writes the
// variable through an address the method gave away is not compared: desktop's optimized build reads the vector
// before the call and loses the call's write, while its Debug build, like NetWasm, keeps it;
// VectorLocalLaneStoreRewriterTests check that such a store stays a store to memory.
public sealed class VectorLocalLaneStoreCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void VectorLocalsWrittenThroughTheirAddressMatchDesktop() =>
        runner.Run(new("VectorLocalLaneStore", "VectorLocalLaneStore", Source, [0, 1, 3, 7])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;
        namespace VectorLocalLaneStore;

        public struct Particle
        {
            public int Id;
            public Vector3 Position;
            public float Mass;
            public Vector2 Velocity;
        }

        public static class EntryPoint
        {
            private static int _trace;
            private static int _hash;

            public static int Trace() => _trace;

            private static void Mix(float value) => _hash = unchecked(_hash * 31 + BitConverter.SingleToInt32Bits(value));
            private static void Mix(Vector2 value) { Mix(value.X); Mix(value.Y); }
            private static void Mix(Vector3 value) { Mix(value.X); Mix(value.Y); Mix(value.Z); }
            private static void Mix(Vector4 value) { Mix(value.X); Mix(value.Y); Mix(value.Z); Mix(value.W); }
            private static void Mix(Quaternion value) { Mix(value.X); Mix(value.Y); Mix(value.Z); Mix(value.W); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Throwing(int input) => input > 2 ? throw new InvalidOperationException() : input;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Scale(ref Vector3 v, float by) { v.X *= by; v.Z = v.Z * by + 1; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float Weighted(in Vector4 v) => v.X + v.Y * 3 + v.Z * 5 + v.W * 7;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Vector4 Argument(Vector4 v, float a)
            {
                v.X = a;
                v.W += v.X;
                return v;
            }

            public static int Run(int input)
            {
                _hash = 17;
                var a = input * 0.75f - 1.5f;

                // Stores, compound assignments of each form, reads in between.
                var v = new Vector4(a, a + 1, a + 2, a + 3);
                v.Y = a * 2;
                v.Z += v.X;
                v.W = v.Y - v.Z;
                v.X *= 3;
                Mix(v);
                var two = new Vector2(a, -a);
                two.Y += 4;
                two.X = two.Y * two.X;
                Mix(two);
                var three = new Vector3(a);
                three.Z -= 2;
                Mix(three.Length()); Mix(three);
                var q = new Quaternion(a, 1, 2, 3);
                q.W += a;
                q.X = q.W * 2;
                Mix(q);

                // An indexer, a ref local, an Unsafe.As view, inlined ref and in parameters.
                var indexed = new Vector4(a);
                indexed[2] = a + 4;
                indexed[0] += 1;
                Mix(indexed);
                var target = new Vector4(a);
                ref var r = ref target;
                r.X = a + 5;
                r.Z += 1;
                Mix(target);
                var viewed = new Vector4(a);
                Unsafe.As<Vector4, Vector128<float>>(ref viewed) = Vector128.Create(a, a * 2, a * 3, a * 4);
                viewed.Z = 1;
                Mix(viewed);
                var scaled = new Vector3(a, 2, 3);
                Scale(ref scaled, 2);
                Mix(scaled);
                var weighed = new Vector4(a, 2, 3, 4);
                weighed.Y = Weighted(in weighed);
                Mix(weighed);
                Mix(Argument(new Vector4(1, 2, 3, 4), a));

                // A struct's vector fields.
                var p = new Particle { Id = input, Position = new Vector3(a, 2, 3), Mass = 4, Velocity = new Vector2(1, a) };
                p.Position.X = a + 1;
                p.Position.Y += p.Mass;
                p.Velocity.Y *= 2;
                Mix(p.Position); Mix(p.Velocity); Mix(p.Mass + p.Id);

                // The vector is stored between its address and the lane store.
                var other = new Vector4(9, 8, 7, 6);
                var replaced = new Vector4(a);
                replaced.X = (replaced = other).Y;
                Mix(replaced);
                // A lane store nested in another's value.
                var nested = new Vector4(a);
                nested.X = nested.Y = a + 2;
                Mix(nested);
                // A throw between the address and the store leaves the vector as it was.
                var guarded = new Vector4(a, 1, 2, 3);
                try { guarded.Y = Throwing(input); } catch (InvalidOperationException) { Mix(-1f); }
                Mix(guarded);
                var values = new float[input + 1];
                for (var i = 0; i < values.Length; i++) values[i] = i - a;
                var sum = Vector4.Zero;
                try
                {
                    for (var i = 0; i <= values.Length; i++)
                    {
                        sum.X += values[i];
                        sum.Y = MathF.Max(sum.Y, values[i]);
                        sum.W += 1;
                    }
                }
                catch (IndexOutOfRangeException) { Mix(-2f); }
                Mix(sum);

                _trace = _hash;
                return _hash;
            }
        }
        """;
}
