// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Accesses of one local's or argument's array within a block read its length once. Each shape here gives a later
// access a reason to need its own: the slot is stored in between, the array is null, an index in the middle of the
// run is out of range after earlier stores happened, two slots hold different arrays or the same one, the slot is
// stored in each iteration of a loop, elements are structs or covariant references, and lengths are read directly.
public sealed class ArrayLengthRunCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AccessesSharingALengthBehaveAsDesktopDoes() =>
        runner.Run(new("ArrayLengthRuns", "ArrayLengthRuns", Source, [0, 1, 2, 3, 5, 9])
        {
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ArrayLengthRuns;

        public struct Point
        {
            public int X;
            public int Y;
        }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                var hash = 17;
                hash = Mix(hash, Guard(() => Writes(input)));
                hash = Mix(hash, Guard(() => Restored(input)));
                hash = Mix(hash, Guard(() => Nulls(input)));
                hash = Mix(hash, Guard(() => MiddleOutOfRange(input)));
                hash = Mix(hash, Guard(() => TwoSlots(input)));
                hash = Mix(hash, Guard(() => Rows(input)));
                hash = Mix(hash, Guard(() => Structs(input)));
                hash = Mix(hash, Guard(() => Covariant(input)));
                hash = Mix(hash, Guard(() => Lengths(new int[input], input)));
                hash = Mix(hash, Guard(() => Arguments(new byte[input + 2], input)));
                _trace = hash;
                return hash;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            private static int Guard(Func<int> run)
            {
                try
                {
                    return run();
                }
                catch (NullReferenceException)
                {
                    return -1 + _trace;
                }
                catch (IndexOutOfRangeException)
                {
                    return -2 + _trace;
                }
                catch (ArrayTypeMismatchException)
                {
                    return -3 + _trace;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Writes(int input)
            {
                var bytes = new byte[input + 3];
                bytes[0] = (byte)input;
                bytes[1] = (byte)(input >> 8);
                bytes[2] = 7;
                _trace = bytes[0] + bytes.Length;
                bytes[3] = 9;
                return bytes[0] * 7 + bytes[3];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Restored(int input)
            {
                var bytes = new byte[2];
                bytes[0] = 1;
                bytes[1] = 2;
                bytes = new byte[input + 1];
                bytes[0] = 3;
                _trace = bytes.Length;
                bytes[1] = 4;
                return bytes.Length * 10 + bytes[0];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Nulls(int input)
            {
                var values = input > 4 ? null : new int[input + 1];
                values![0] = 5;
                _trace = values[0];
                values[input] = 6;
                return values[0] + values[input];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MiddleOutOfRange(int input)
            {
                var values = new int[3];
                _trace = 0;
                values[0] = 1;
                _trace = values[0];
                values[input] = 2;
                _trace += 100;
                values[2] = 3;
                return values[0] + values[1] + values[2];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoSlots(int input)
            {
                var a = new int[input + 1];
                var b = input % 2 == 0 ? a : new int[2];
                a[0] = 1;
                b[0] = 2;
                a[input] = 3;
                b[1 % b.Length] = 4;
                return a[0] * 100 + b[0] * 10 + a.Length + b.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Rows(int input)
            {
                var rows = new int[input + 1][];
                for (var i = 0; i < rows.Length; i++) rows[i] = new int[i + 2];
                var sum = 0;
                for (var i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    row[0] = i;
                    row[1] = i * 2;
                    row[row.Length - 1] += 1;
                    sum = sum * 3 + row[0] + row[1] + row.Length;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Structs(int input)
            {
                var points = new Point[input + 1];
                points[0].X = 4;
                points[0].Y = 5;
                points[input].X += 6;
                ref var last = ref points[input];
                last.Y = 7;
                return points[0].X * 1000 + points[0].Y * 100 + points[input].X * 10 + points[input].Y;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Covariant(int input)
            {
                object[] items = input % 3 == 0 ? new string[3] : new object[3];
                items[0] = "a";
                _trace = items.Length;
                items[1] = input;
                items[2] = "c";
                return items.Length + (items[1] is int ? 10 : 0);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lengths(int[] values, int input)
            {
                var first = values.Length;
                values[input - 1] = 2;
                return first * 10 + values.Length + values[input - 1];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Arguments(byte[] bytes, int input)
            {
                bytes[0] = 1;
                bytes[input + 1] = 2;
                bytes[input] += 3;
                return bytes[0] + bytes[input] * 10 + bytes[input + 1] * 100 + bytes.Length;
            }
        }
        """;
}
