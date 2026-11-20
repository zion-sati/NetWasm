// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A Vector128's Vector64 halves, as desktop .NET has them, for every element type: GetLower, GetUpper, WithLower,
// WithUpper, Create from two halves (generic and per type), Vector64's ToVector128 and ToVector128Unsafe's lower half,
// the halves round-tripped, inside a loop and through a transpose, and an unsupported element type's
// NotSupportedException. Each result mixes in as its 64-bit lanes, so pointer-sized elements hash alike on every target.
public sealed class Vector64HalvesCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void Vector64HalvesBehaveAsDesktopDoes() =>
        runner.Run(new("Vector64Halves", "Vector64Halves", Source, [0, 1, 5, 9])
        {
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;
        namespace Vector64Halves;

        public static class EntryPoint
        {
            private static int _trace;
            private static int _hash;

            public static int Trace() => _trace;

            private static void Mix(ulong value) => _hash = unchecked(_hash * 31 + (int)value + (int)(value >> 32));
            private static void Mix<T>(Vector64<T> value) where T : struct => Mix(value.As<T, ulong>().ToScalar());
            private static void Mix<T>(Vector128<T> value) where T : struct
            {
                Mix(value.AsUInt64().GetElement(0));
                Mix(value.AsUInt64().GetElement(1));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Check<T>(int input) where T : struct
            {
                var seed = (ulong)input * 0x9E3779B97F4A7C15UL + 0x0123456789ABCDEFUL;
                var vector = Vector128.Create(seed, ~seed * 3).As<ulong, T>();
                var half = Vector64.Create(seed ^ 0xA5A55A5AF00F0FF0UL).As<ulong, T>();
                var lower = vector.GetLower();
                var upper = vector.GetUpper();
                Mix(lower);
                Mix(upper);
                Mix(vector.WithLower(half));
                Mix(vector.WithUpper(half));
                Mix(Vector128.Create(upper, lower));
                Mix(half.ToVector128());
                Mix(half.ToVector128Unsafe().GetLower());
                Mix(Vector128.Create(lower, upper) == vector ? 1UL : 0UL);
                Mix(Vector128.Create(lower, upper).WithUpper(lower).GetUpper());
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Vector128<float> Transpose(Vector128<float> t0, Vector128<float> t2) =>
                Vector128.Create(t0.AsUInt64().GetLower(), t2.AsUInt64().GetUpper()).AsSingle();

            public static int Run(int input)
            {
                _hash = 17;
                Check<byte>(input); Check<sbyte>(input); Check<short>(input); Check<ushort>(input);
                Check<int>(input); Check<uint>(input); Check<long>(input); Check<ulong>(input);
                Check<float>(input); Check<double>(input); Check<nint>(input); Check<nuint>(input);

                // Per-type overloads, a loop that carries a half, and a transpose.
                var bytes = Vector128.Create((byte)input, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
                Mix(Vector128.Create(bytes.GetUpper(), bytes.GetLower()));
                Mix(Vector128.Create(Vector64.Create(input * 1.5), Vector64.Create(-2.0)));
                var acc = Vector128.Create((ulong)input, 1UL);
                for (var i = 0; i < input + 3; i++)
                    acc = Vector128.Create(acc.GetUpper() + Vector64.Create((ulong)i), acc.GetLower());
                Mix(acc);
                Mix(Transpose(Vector128.Create(1f, 2f, input, 4f), Vector128.Create(5f, 6f, 7f, -input)));

                // Bool is no vector element type, as upstream's ThrowHelper says.
                try
                {
                    Mix(Vector128.Create(default(Vector64<bool>), default(Vector64<bool>)).GetHashCode() == 0 ? 1UL : 2UL);
                }
                catch (NotSupportedException)
                {
                    Mix(3UL);
                }
                _trace = _hash;
                return _hash;
            }
        }
        """;
}
