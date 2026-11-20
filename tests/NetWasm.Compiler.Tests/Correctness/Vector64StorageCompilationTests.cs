// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A Vector64<T> is a v128 value in its low 8 bytes. Each case keeps one where a value type can live (fields between
// other fields, statics, arrays, spans, boxes, generics, closures, a Vector128's halves through the core library's
// software paths), passes it by value to a callee that changes its copy, writes it through its address, and runs
// Vector64's own operations, comparing the results with desktop .NET. Each value mixes in as its 64 bits, so lanes
// above the storage never show.
public sealed class Vector64StorageCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void Vector64StorageMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new("Vector64Storage", "Vector64Storage", Source, [0, 1, 2, 5])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Runtime.Intrinsics;
        namespace Vector64Storage;

        public struct Packed
        {
            public byte Before;
            public Vector64<short> Half;
            public int Middle;
            public Vector64<float> Floats;
            public byte After;
        }

        public sealed class Holder
        {
            public Vector64<int> Field;
            public Packed Nested;
        }

        public struct Box<T>
        {
            public T Value;
            public Box(T value) => Value = value;
        }

        public static class EntryPoint
        {
            private static int _trace;
            private static int _hash;
            private static Vector64<long> _static;

            public static int Trace() => _trace;

            private static void Mix(ulong value) => _hash = unchecked(_hash * 31 + (int)value + (int)(value >> 32));
            private static void Mix<T>(Vector64<T> value) where T : struct => Mix(value.AsUInt64().ToScalar());
            private static void Mix<T>(Vector128<T> value) where T : struct
            {
                Mix(value.AsUInt64().GetElement(0));
                Mix(value.AsUInt64().GetElement(1));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Vector64<int> Changed(Vector64<int> copy, int by)
            {
                copy += Vector64.Create(by);
                return copy;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Write(out Vector64<float> result, float x) => result = Vector64.Create(x, -x);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref Vector64<ushort> value) => value = value + Vector64<ushort>.One;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static T Through<T>(T value) => value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object Boxed<T>(T value) => value!;

            public static int Run(int input)
            {
                _hash = 17;
                var a = Vector64.Create(input, input * 3 + 1);
                var b = Vector64.Create((short)input, (short)-2, (short)(input << 4), short.MaxValue);
                var f = Vector64.Create(input * 0.5f, -1.25f);
                Mix(a); Mix(b); Mix(f);

                // Arithmetic, comparisons and conversions in software.
                Mix(a + a); Mix(a * Vector64.Create(7)); Mix(a - Vector64<int>.One); Mix(-a); Mix(~a);
                Mix(a & Vector64.Create(0xFF)); Mix(a | Vector64.Create(0x100)); Mix(a ^ a);
                Mix(Vector64.Max(b, Vector64<short>.Zero)); Mix(Vector64.Min(f, Vector64.Create(0f)));
                Mix(Vector64.Equals(a, Vector64.Create(input))); Mix(Vector64.ConvertToSingle(a));
                Mix(Vector64.ShiftLeft(b, 3)); Mix(Vector64.Abs(b)); Mix((ulong)Vector64.Sum(a));
                Mix(a == Vector64.Create(input, input * 3 + 1) ? 1UL : 2UL);
                Mix((ulong)a.GetElement(1));
                Mix(a.WithElement(0, 42)); Mix(Vector64.Narrow(a, a));

                // By value, by reference and out.
                var original = a;
                Mix(Changed(a, 5)); Mix(a); Mix(original == a ? 3UL : 4UL);
                Write(out var written, input);
                Mix(written);
                var bumped = Vector64.Create((ushort)input);
                Bump(ref bumped);
                Mix(bumped);

                // Fields between other fields, nested, and statics.
                var packed = new Packed { Before = 0xAB, Half = b, Middle = -7, Floats = f, After = 0xCD };
                packed.Half = packed.Half + Vector64<short>.One;
                Mix(packed.Before); Mix(packed.Half); Mix((ulong)packed.Middle); Mix(packed.Floats); Mix(packed.After);
                var holder = new Holder { Field = a, Nested = packed };
                holder.Field = Vector64.Create(9, input);
                holder.Nested.Floats = -holder.Nested.Floats;
                Mix(holder.Field); Mix(holder.Nested.Floats); Mix(holder.Nested.After);
                _static = Vector64.Create((long)input * 1000003);
                Mix(_static);

                // Arrays, spans, lists and memory views.
                var array = new Vector64<int>[5];
                for (var i = 0; i < array.Length; i++) array[i] = Vector64.Create(i * input, -i);
                array[2] += array[4];
                Mix(array[2]); Mix(array[4]);
                Span<Vector64<int>> span = array;
                span[1] = span[3] * span[3];
                Mix(span[1]); Mix(MemoryMarshal.AsBytes(span)[13]);
                Span<Vector64<byte>> stacked = stackalloc Vector64<byte>[3];
                stacked[1] = Vector64.Create((byte)input);
                Mix(stacked[0]); Mix(stacked[1]);
                var list = new List<Vector64<float>> { f, -f };
                list[1] += Vector64<float>.One;
                Mix(list[1]);
                Mix(Unsafe.As<Vector64<int>, long>(ref array[3]) == array[3].AsInt64().ToScalar() ? 5UL : 6UL);

                // Generics, boxes and closures.
                Mix(Through(a)); Mix(new Box<Vector64<short>>(b).Value);
                var boxed = Boxed(f);
                Mix(boxed is Vector64<float> unboxed ? unboxed : default);
                Mix(boxed.Equals(f) ? 7UL : 8UL);
                Func<Vector64<int>, Vector64<int>> add = v => v + a;
                Mix(add(Vector64.Create(1)));

                // A Vector128's halves through its fields and the core library's software paths.
                var wide = Vector128.Create(a, Vector64.Create(input * 11, -3));
                Mix(wide); Mix(wide.GetLower()); Mix(wide.GetUpper());
                Mix(wide.WithUpper(a)); Mix(wide.WithLower(Vector64.Create(-1)));
                Mix(Vector128.Create(b, b) + Vector128<short>.One);
                Mix(a.ToVector128()); Mix(a.ToVector128Unsafe().GetLower());
                Mix(Vector128.Create(f, f).GetUpper());
                Mix(Vector64.Create(wide.GetUpper().ToScalar()));

                _trace = _hash;
                return _hash;
            }
        }
        """;
}
