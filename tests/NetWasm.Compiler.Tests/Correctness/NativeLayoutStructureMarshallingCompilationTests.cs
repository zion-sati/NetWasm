// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Marshal's structure members on a struct that holds a bool or char use the native layout desktop .NET's runtime
// marshalling gives it on Unix: SizeOf and OffsetOf report it, StructureToPtr writes each field converted (a bool as a
// 4-byte BOOL or, with [MarshalAs(U1)], a byte; a char as one ANSI byte or, under CharSet.Unicode, its UTF-16 code
// unit; nested structs in their own native layout), and PtrToStructure reads it back (a native boolean true when it is
// not zero, an ANSI byte past ASCII as U+FFFD). Compared with desktop .NET, byte for byte.
public sealed class NativeLayoutStructureMarshallingCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NativeLayoutStructuresMatchDesktop() =>
        runner.Run(new("NativeLayoutStructureMarshalling", "NetWasm.Correctness.NativeLayout", Source, [0, 1, 7])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace NetWasm.Correctness.NativeLayout;

        public struct Flagged { public int A; public bool B; public int C; }
        public struct AnsiPair { public byte X; public char C; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct WidePair { public byte X; public char C; }
        public struct ByteFlag { public int A; [MarshalAs(UnmanagedType.U1)] public bool B; }
        public struct Outer { public short S; public Flagged Inner; public bool Flag; public char Letter; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct Packed { public byte X; public bool B; public long L; }
        [StructLayout(LayoutKind.Explicit)] public struct Overlaid { [FieldOffset(0)] public int I; [FieldOffset(8)] public bool B; }

        public static unsafe class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value + (int)(value >> 32));

            // The struct's native bytes (padding left as it was) and the value read back from them.
            private static T Bytes<T>(ref int hash, T value) where T : struct
            {
                var size = Marshal.SizeOf<T>();
                var native = Marshal.AllocHGlobal(size);
                try
                {
                    new Span<byte>((void*)native, size).Fill(0xAA);
                    Marshal.StructureToPtr(value, native, false);
                    hash = Mix(hash, size);
                    foreach (var b in new ReadOnlySpan<byte>((void*)native, size)) hash = Mix(hash, b);
                    var back = Marshal.PtrToStructure<T>(native);
                    Marshal.DestroyStructure<T>(native);
                    return back;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            private static int Flag(bool value) => value ? 1 : 0;

            private static T Read<T>(ReadOnlySpan<byte> bytes) where T : struct
            {
                var native = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    bytes.CopyTo(new Span<byte>((void*)native, bytes.Length));
                    return Marshal.PtrToStructure<T>(native);
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            public static int Run(int input)
            {
                var hash = 17;
                var flaggedBack = Bytes(ref hash, new Flagged { A = input, B = input != 0, C = 3 });
                hash = Mix(hash, flaggedBack.A + Flag(flaggedBack.B) * 10 + flaggedBack.C * 100);
                var ansiBack = Bytes(ref hash, new AnsiPair { X = 2, C = input == 1 ? '\u00e9' : 'A' });
                hash = Mix(hash, ansiBack.X + ansiBack.C * 10);
                var wideBack = Bytes(ref hash, new WidePair { X = 2, C = '\u20ac' });
                hash = Mix(hash, wideBack.X + wideBack.C * 10);
                var byteBack = Bytes(ref hash, new ByteFlag { A = 5, B = true });
                hash = Mix(hash, byteBack.A + Flag(byteBack.B) * 10);
                var outerBack = Bytes(ref hash, new Outer { S = -2, Inner = new() { A = 1, B = true, C = input }, Flag = true, Letter = 'z' });
                hash = Mix(hash, outerBack.S + outerBack.Inner.A * 10 + Flag(outerBack.Inner.B) * 100 + outerBack.Inner.C * 1000 +
                    Flag(outerBack.Flag) * 100000 + outerBack.Letter * 1000000L);
                var packedBack = Bytes(ref hash, new Packed { X = 1, B = true, L = input * 1000003L });
                hash = Mix(hash, packedBack.X + Flag(packedBack.B) * 10 + packedBack.L * 100);
                var overlaidBack = Bytes(ref hash, new Overlaid { I = input, B = true });
                hash = Mix(hash, overlaidBack.I + Flag(overlaidBack.B) * 10);
                foreach (var name in new[] { "A", "B", "C" }) hash = Mix(hash, (long)Marshal.OffsetOf<Flagged>(name));
                foreach (var name in new[] { "S", "Inner", "Flag", "Letter" }) hash = Mix(hash, (long)Marshal.OffsetOf<Outer>(name));
                hash = Mix(hash, (long)Marshal.OffsetOf<Packed>("L"));
                hash = Mix(hash, Marshal.SizeOf<Outer>() * 100 + Marshal.SizeOf<WidePair>());

                // Native booleans other than 1 read as true; an ANSI byte past ASCII reads as U+FFFD.
                var flagged = Read<Flagged>([7, 0, 0, 0, 0, 1, 0, 0, 9, 0, 0, 0]);
                var pair = Read<AnsiPair>([4, 0x80]);
                var plain = Read<AnsiPair>([4, (byte)'q']);
                hash = Mix(hash, flagged.A + (flagged.B ? 100 : 0) + flagged.C * 1000);
                hash = Mix(hash, pair.X + pair.C * 10 + plain.C * 100000L);
                _trace = hash;
                return hash;
            }
        }
        """;
}
