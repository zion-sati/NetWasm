// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Marshal's structure members on inline arrays and fixed buffers, as desktop .NET's runtime marshalling converts them
// on Unix. A ByValArray holds its first SizeConst elements inline: blittable elements copied, booleans as 4-byte BOOLs
// or, with ArraySubType U1, bytes, and characters as ANSI bytes or UTF-16 under CharSet.Unicode; a shorter array is
// rejected and a null one writes zeros, and PtrToStructure reads a new array of SizeConst elements. A fixed buffer of
// bools or ANSI characters is a struct of one converting field, so desktop converts only its first element; a fixed
// buffer that converts nothing is copied whole. Compared with desktop .NET, byte for byte.
public sealed class InlineArrayStructureMarshallingCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void InlineArraysAndFixedBuffersMatchDesktop() =>
        runner.Run(new("InlineArrayStructureMarshalling", "NetWasm.Correctness.InlineArrays", Source, [0, 1, 7])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        #nullable disable
        using System;
        using System.Runtime.InteropServices;

        namespace NetWasm.Correctness.InlineArrays;

        public struct Numbers { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] Items; public int B; }
        public struct Flags { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public bool[] Values; public int B; }
        public struct ByteFlags { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U1)] public bool[] Values; public int B; }
        public struct ByteRow { public short A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public byte[] Values; public bool B; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct WideChars { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public char[] Chars; public int B; }
        public unsafe struct FixedFlags { public int A; public fixed bool Values[4]; public int B; }
        public unsafe struct FixedChars { public int A; public fixed char Chars[3]; public int B; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public unsafe struct FixedWideChars { public int A; public fixed char Chars[3]; public int B; }
        public unsafe struct FixedInts { public int A; public fixed int Items[3]; public bool B; }

        public static unsafe class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value + (int)(value >> 32));

            // The structure's native bytes and the value read back, or the failure desktop gives it.
            private static int Bytes<T>(int hash, T value, Func<T, long> back) where T : struct
            {
                var size = Marshal.SizeOf<T>();
                var native = Marshal.AllocHGlobal(size);
                try
                {
                    new Span<byte>((void*)native, size).Fill(0xAA);
                    Marshal.StructureToPtr(value, native, false);
                    hash = Mix(hash, size);
                    foreach (var b in new ReadOnlySpan<byte>((void*)native, size)) hash = Mix(hash, b);
                    hash = Mix(hash, back(Marshal.PtrToStructure<T>(native)));
                    Marshal.DestroyStructure<T>(native);
                    return hash;
                }
                catch (ArgumentException)
                {
                    return Mix(hash, 1);
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            private static long Sum(int[] values)
            {
                long sum = values.Length;
                foreach (var value in values) sum = sum * 131 + value;
                return sum;
            }

            private static long Sum(bool[] values)
            {
                long sum = values.Length;
                foreach (var value in values) sum = sum * 3 + (value ? 1 : 0);
                return sum;
            }

            public static int Run(int input)
            {
                var hash = 17;
                hash = Bytes(hash, new Numbers { A = 1, Items = new[] { 2, 3, 4, 5 + input }, B = 6 }, back => Sum(back.Items) + back.B);
                hash = Bytes(hash, new Numbers { A = 1, Items = new[] { 2 }, B = 6 }, back => Sum(back.Items));
                hash = Bytes(hash, new Numbers { A = 1, Items = new[] { 2, 3, 4, 5, 6, 7 }, B = 6 }, back => Sum(back.Items));
                hash = Bytes(hash, new Numbers { A = 1, Items = null, B = 6 }, back => Sum(back.Items) + back.B);
                hash = Bytes(hash, new Flags { A = 1, Values = new[] { true, input != 0, true }, B = 6 }, back => Sum(back.Values) + back.B);
                hash = Bytes(hash, new ByteFlags { A = 1, Values = new[] { true, false, input == 1 }, B = 6 }, back => Sum(back.Values) + back.B);
                hash = Bytes(hash, new ByteRow { A = 1, Values = new byte[] { 1, 2, 3, 4, (byte)input }, B = true }, back => back.Values[4] + back.Values.Length * 10 + (back.B ? 100 : 0));
                hash = Bytes(hash, new WideChars { A = 1, Chars = new[] { 'a', 'é', (char)('c' + input) }, B = 6 }, back => back.Chars[0] + back.Chars[1] * 1000L + back.Chars[2] * 1000000L);
                foreach (var name in new[] { "Items", "B" }) hash = Mix(hash, (long)Marshal.OffsetOf<Numbers>(name));
                hash = Mix(hash, (long)Marshal.OffsetOf<Flags>("B"));

                var flags = new FixedFlags { A = 1, B = 6 };
                flags.Values[0] = true;
                flags.Values[2] = input != 0;
                hash = Bytes(hash, flags, back => (back.Values[0] ? 1 : 0) + (back.Values[1] ? 10 : 0) + (back.Values[2] ? 100 : 0) + back.B * 1000L);
                var chars = new FixedChars { A = 1, B = 6 };
                chars.Chars[0] = 'a';
                chars.Chars[1] = 'b';
                chars.Chars[2] = (char)('c' + input);
                hash = Bytes(hash, chars, back => back.Chars[0] + back.Chars[1] * 1000L + back.Chars[2] * 1000000L + back.B);
                var wide = new FixedWideChars { A = 1, B = 6 };
                wide.Chars[0] = 'a';
                wide.Chars[1] = 'é';
                wide.Chars[2] = (char)('c' + input);
                hash = Bytes(hash, wide, back => back.Chars[0] + back.Chars[1] * 1000L + back.Chars[2] * 1000000L + back.B);
                var ints = new FixedInts { A = 1, B = true };
                ints.Items[0] = 7;
                ints.Items[2] = 9 + input;
                hash = Bytes(hash, ints, back => back.Items[0] + back.Items[1] * 100 + back.Items[2] * 10000L + (back.B ? 1 : 0));
                _trace = hash;
                return hash;
            }
        }
        """;
}
