// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Roslyn reads a constant ReadOnlySpan of an enum from data as it does its underlying type's: through
// RuntimeHelpers.CreateSpan<TEnum> for wider enums, and a pointer span over the field for byte-sized ones, including a
// params ReadOnlySpan<TEnum> argument list. Each enum width, signed and unsigned, flags included, is read, indexed and
// searched, with arrays of enums initialized from data beside them, and compared with desktop .NET.
public sealed class EnumConstantSpanCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ConstantSpansAndArraysOfEnumsMatchDesktop() =>
        runner.Run(new("EnumConstantSpans", "EnumConstantSpans", Source, [0, 1, 5])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        namespace EnumConstantSpans;

        public enum Small : byte { A = 1, B = 7, C = 200 }
        public enum Short : short { A = -3, B = 300, C = 30000 }
        public enum Medium { A = -1, B = 5, C = 1 << 20 }
        public enum Wide : long { A = long.MinValue, B = 3, C = 1L << 40 }
        [Flags] public enum Flags : ushort { None = 0, X = 1, Y = 0x8000 }
        public enum Unsigned : uint { A = 1, B = 0xFFFF_FFFF }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value + (int)(value >> 32));

            private static bool HasC(params ReadOnlySpan<Medium> values) => values.Contains(Medium.C);

            private static int IndexOfShort(Short value)
            {
                ReadOnlySpan<Short> values = [Short.A, Short.B, Short.C, Short.B];
                return values.IndexOf(value);
            }

            public static int Run(int input)
            {
                var hash = 17;
                ReadOnlySpan<Small> small = [Small.A, Small.B, Small.C, Small.B];
                ReadOnlySpan<Short> shorts = [Short.A, Short.B, Short.C];
                ReadOnlySpan<Medium> medium = [Medium.A, Medium.B, Medium.C, Medium.A, Medium.B];
                ReadOnlySpan<Wide> wide = [Wide.A, Wide.B, Wide.C];
                ReadOnlySpan<Flags> flags = [Flags.X, Flags.Y, Flags.X | Flags.Y];
                ReadOnlySpan<Unsigned> unsigned = [Unsigned.A, Unsigned.B];
                hash = Mix(hash, small.Length + shorts.Length * 10 + medium.Length * 100 + wide.Length * 1000 +
                    flags.Length * 10000 + unsigned.Length * 100000);
                foreach (var value in small) hash = Mix(hash, (long)value);
                foreach (var value in shorts) hash = Mix(hash, (long)value);
                foreach (var value in medium) hash = Mix(hash, (long)value);
                foreach (var value in wide) hash = Mix(hash, (long)value);
                foreach (var value in flags) hash = Mix(hash, (long)value);
                foreach (var value in unsigned) hash = Mix(hash, (long)value);
                hash = Mix(hash, (long)small[input % small.Length] + (long)medium[input % medium.Length]);
                hash = Mix(hash, small.IndexOf(Small.C) * 10 + small.LastIndexOf(Small.B));
                hash = Mix(hash, shorts.Contains((Short)(300 - input)) ? 1 : 2);
                hash = Mix(hash, medium.Contains(Medium.C) ? 3 : 4);
                hash = Mix(hash, medium.IndexOf((Medium)(5 * input)));
                hash = Mix(hash, wide.Contains(Wide.C) ? 5 : 6);
                hash = Mix(hash, flags.IndexOf(Flags.Y));
                hash = Mix(hash, unsigned.Contains(Unsigned.B) ? 7 : 8);
                hash = Mix(hash, HasC(Medium.A, Medium.C) ? 9 : 10);
                hash = Mix(hash, HasC(Medium.A, (Medium)input) ? 11 : 12);
                hash = Mix(hash, IndexOfShort(Short.C) * 10 + IndexOfShort((Short)input));
                hash = Mix(hash, medium.SequenceEqual([Medium.A, Medium.B, Medium.C, Medium.A, Medium.B]) ? 13 : 14);
                hash = Mix(hash, medium.ToArray().Length);

                // Arrays of enums initialized from data.
                var array = new Medium[] { Medium.A, Medium.B, Medium.C, Medium.C };
                array[input % array.Length] = Medium.B;
                foreach (var value in array) hash = Mix(hash, (long)value);
                var wideArray = new Wide[] { Wide.C, Wide.A, Wide.B };
                hash = Mix(hash, (long)wideArray[input % wideArray.Length]);
                var smallArray = new Small[] { Small.C, Small.A, Small.B, Small.C };
                hash = Mix(hash, Array.IndexOf(smallArray, Small.B) * 10 + (int)smallArray[input % smallArray.Length]);

                _trace = hash;
                return hash;
            }
        }
        """;
}
