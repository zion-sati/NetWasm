// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Marshal's structure members where NetWasm and desktop .NET's runtime marshalling on Unix used to part: the
// constructors PtrToStructure runs (a class's or a structure's parameterless one, public or not, through either
// overload), a formatted class embedded in a structure or a class, ByValArray arrays of structures (blittable or
// not, shorter or longer than SizeConst, or null), SafeHandle and CriticalHandle fields, a sequential class whose
// base has no layout, and ANSI characters outside ASCII, alone or in a ByValArray, written and read; and what a
// failed conversion leaves: StructureToPtr clears the native structure and writes no field (a SafeHandle, a null
// CriticalHandle, a short ByValArray, text that does not fit, the same in an embedded class or a later array
// element), and PtrToStructure fills the fields before the one that fails. Each case returns a hash of what it saw,
// or the kind of exception desktop throws.
public sealed class StructureMarshallingGapsCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StructureMarshallingMatchesDesktopWhereItUsedToPart() =>
        runner.Run(new("StructureMarshallingGaps", "NetWasm.Correctness.StructureGaps", Source, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
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
        using Microsoft.Win32.SafeHandles;

        namespace NetWasm.Correctness.StructureGaps;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct FailTStr { public int A; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)] public string S; public int B; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct FailAnsi { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public char[] C; public int B; }
        [StructLayout(LayoutKind.Sequential)]
        public struct FailShort { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public int[] Arr; public int B; }
        [StructLayout(LayoutKind.Sequential)]
        public struct FailSafe { public int A; public Handle H; public int B; }
        [StructLayout(LayoutKind.Sequential)]
        public struct FailStringThenShort
        {
            public int A;
            [MarshalAs(UnmanagedType.LPStr)] public string S;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] Arr;
            public int B;
        }
        public sealed class FailCritical : CriticalHandle
        {
            public FailCritical() : base(IntPtr.Zero) { }
            public override bool IsInvalid => true;
            protected override bool ReleaseHandle() => true;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct FailCriticalField { public int A; public FailCritical H; public int B; }
        [StructLayout(LayoutKind.Sequential)]
        public sealed class FailInner { public int X = 1; public Handle H; }
        [StructLayout(LayoutKind.Sequential)]
        public sealed class FailOuter { public int A = 7; public FailInner I = new(); public int B = 8; }
        [StructLayout(LayoutKind.Sequential)]
        public struct FailElement { public int Tag; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] Arr; }
        [StructLayout(LayoutKind.Sequential)]
        public struct FailElements { public int A; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public FailElement[] Items; }
        [StructLayout(LayoutKind.Sequential)]
        public sealed class FailSafeClass { public int A = 7; public Handle H; public int B = 8; }

        [StructLayout(LayoutKind.Sequential)]
        public class Counted
        {
            public int A;
            public long B;
            public Counted() { EntryPoint.Constructed++; A = 5; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public class Hidden
        {
            public int A;
            private Hidden() { EntryPoint.Constructed += 10; }
            public static Hidden Make(int a) => new() { A = a };
        }

        public struct WithConstructor
        {
            public int A;
            public WithConstructor() { EntryPoint.Constructed += 100; A = 3; }
        }

        [StructLayout(LayoutKind.Sequential)] public class Inner { public int X; public short Y; }
        public struct Outer { public byte Tag; public Inner Inner; public int After; }
        [StructLayout(LayoutKind.Sequential)] public class OuterClass { public int A; public Inner Inner; public bool Flag; }

        public struct Point { public int X; public short Y; }
        public struct Polygon { public int Count; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public Point[] Points; public int After; }
        public struct Flag { public bool On; public char C; }
        public struct Flags { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public Flag[] Items; public byte After; }
        public enum Shade : short { Light = 1, Dark = 300 }
        public struct Shades { public byte Tag; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public Shade[] Items; public int After; }

        public sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public Handle() : base(true) { }
            public Handle(IntPtr value) : base(true) { SetHandle(value); }
            protected override bool ReleaseHandle() => true;
        }

        public struct HoldsHandle { public int A; public Handle H; }

        public sealed class Critical : CriticalHandleZeroOrMinusOneIsInvalid
        {
            public Critical() { }
            public Critical(IntPtr value) { SetHandle(value); }
            protected override bool ReleaseHandle() => true;
        }

        public struct HoldsCritical { public int A; public Critical H; }
        [StructLayout(LayoutKind.Sequential)] public class HoldsHandleClass { public int A; public Handle H; }

        public class AutoBase { public int A; }
        [StructLayout(LayoutKind.Sequential)] public class SequentialOnAuto : AutoBase { public int B; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct AnsiChars { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public char[] C; public int After; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct AnsiChar { public char C; public int After; }

        public static unsafe class EntryPoint
        {
            public static int Constructed;
            private static int _trace;

            public static int Trace() => _trace;

            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value + (int)(value >> 32));

            private static int Failure(Action action)
            {
                try { action(); return 0; }
                catch (MissingMethodException) { return 3; }
                catch (TypeLoadException) { return 2; }
                catch (MarshalDirectiveException) { return 6; }
                catch (COMException) { return 4; }
                catch (ArgumentException) { return 1; }
                catch (PlatformNotSupportedException) { return 9; }
                catch (NotSupportedException) { return 5; }
                catch (InvalidOperationException) { return 7; }
                catch (NullReferenceException) { return 8; }
            }

            private static IntPtr Native(int size)
            {
                var native = Marshal.AllocHGlobal(size);
                new Span<byte>((void*)native, size).Fill(0xAA);
                return native;
            }

            // What a failed StructureToPtr left: the exception, whether the native structure is all zero, and whether
            // the bytes after it are untouched (sizes differ with the pointer size, so they are not hashed).
            private static int FailedWrite<T>(T value)
            {
                var size = Marshal.SizeOf<T>();
                var native = Native(size + 8);
                var hash = Failure(() => Marshal.StructureToPtr(value, native, false));
                var zero = new ReadOnlySpan<byte>((void*)native, size).IndexOfAnyExcept((byte)0) < 0;
                var untouched = new ReadOnlySpan<byte>((void*)(native + size), 8).IndexOfAnyExcept((byte)0xAA) < 0;
                Marshal.FreeHGlobal(native);
                return hash * 4 + (zero ? 2 : 0) + (untouched ? 1 : 0);
            }

            private static int FailedWrites()
            {
                var hash = 17;
                hash = Mix(hash, FailedWrite(new FailTStr { A = 1, S = "\u20ac\u20ac\u20ac", B = 2 }));
                hash = Mix(hash, FailedWrite(new FailAnsi { A = 1, C = new[] { '\u20ac', 'a', 'b' }, B = 2 }));
                hash = Mix(hash, FailedWrite(new FailShort { A = 1, Arr = new[] { 7, 8 }, B = 2 }));
                hash = Mix(hash, FailedWrite(new FailSafe { A = 1, H = new Handle(), B = 2 }));
                hash = Mix(hash, FailedWrite(new FailSafe { A = 1, B = 2 }));
                hash = Mix(hash, FailedWrite(new FailStringThenShort { A = 1, S = "x", Arr = new[] { 1 }, B = 2 }));
                hash = Mix(hash, FailedWrite(new FailCriticalField { A = 1, B = 2 }));
                hash = Mix(hash, FailedWrite(new FailOuter()));
                hash = Mix(hash, FailedWrite(new FailElements
                {
                    A = 1,
                    Items = new[] { new FailElement { Tag = 2, Arr = new[] { 3, 4 } }, new FailElement { Tag = 5, Arr = new[] { 6 } } },
                }));
                return hash;
            }

            // A failed PtrToStructure into an existing object: the fields before the one that fails take the native
            // values, an embedded class included, and the ones after keep theirs.
            private static int FailedReads()
            {
                var native = Native(64);
                new Span<byte>((void*)native, 64).Clear();
                Marshal.WriteInt32(native, 5);
                var hash = 17;
                var safe = new FailSafeClass();
                hash = Mix(hash, Failure(() => Marshal.PtrToStructure(native, safe)));
                hash = Mix(Mix(hash, safe.A), safe.B);
                var outer = new FailOuter();
                hash = Mix(hash, Failure(() => Marshal.PtrToStructure(native, outer)));
                hash = Mix(Mix(Mix(hash, outer.A), outer.B), outer.I is null ? -1 : outer.I.X);
                Marshal.FreeHGlobal(native);
                return hash;
            }

            private static int Bytes(int hash, IntPtr native, int size)
            {
                foreach (var b in new ReadOnlySpan<byte>((void*)native, size)) hash = Mix(hash, b);
                return Mix(hash, size);
            }

            public static int Run(int input)
            {
                var hash = input switch
                {
                    0 => ClassConstructors(),
                    1 => HiddenAndStructureConstructors(),
                    2 => EmbeddedClasses(),
                    3 => ArraysOfStructures(),
                    4 => SafeHandles(),
                    5 => SequentialOnAutoBase(),
                    6 => AnsiCharacters(),
                    8 => FailedWrites(),
                    9 => FailedReads(),
                    _ => AnsiReads(),
                };
                _trace = hash;
                return hash;
            }

            // PtrToStructure creates a class with its parameterless constructor, then overwrites what is marshalled.
            private static int ClassConstructors()
            {
                var size = Marshal.SizeOf<Counted>();
                var native = Native(size);
                try
                {
                    Marshal.StructureToPtr(new Counted { A = 7, B = 8 }, native, false);
                    Constructed = 0;
                    var created = (Counted)Marshal.PtrToStructure(native, typeof(Counted));
                    var hash = Mix(17, Constructed);
                    hash = Mix(Mix(hash, created.A), created.B);
                    var generic = Marshal.PtrToStructure<Counted>(native);
                    hash = Mix(hash, Constructed);
                    return Mix(Mix(hash, generic.A), generic.B);
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // A non-public parameterless constructor serves both overloads, as does a structure's own.
            private static int HiddenAndStructureConstructors()
            {
                var native = Native(16);
                try
                {
                    Marshal.StructureToPtr(Hidden.Make(41), native, false);
                    Constructed = 0;
                    var hash = 17;
                    hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.PtrToStructure<Hidden>(native).A)));
                    hash = Mix(hash, Failure(() => hash = Mix(hash, ((Hidden)Marshal.PtrToStructure(native, typeof(Hidden))).A)));
                    hash = Mix(hash, Constructed);
                    *(int*)native = 9;
                    Constructed = 0;
                    hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.PtrToStructure<WithConstructor>(native).A)));
                    hash = Mix(hash, Failure(() => hash = Mix(hash, ((WithConstructor)Marshal.PtrToStructure(native, typeof(WithConstructor))).A)));
                    return Mix(hash, Constructed);
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // A formatted class in a structure or a class is laid out inline, null or not.
            private static int EmbeddedClasses()
            {
                var hash = Mix(Mix(17, Marshal.SizeOf<Outer>()), (int)Marshal.OffsetOf<Outer>("Inner"));
                hash = Mix(Mix(hash, (int)Marshal.OffsetOf<Outer>("After")), Marshal.SizeOf<OuterClass>());
                hash = Mix(hash, (int)Marshal.OffsetOf<OuterClass>("Flag"));
                var size = Marshal.SizeOf<Outer>();
                var native = Native(64);
                try
                {
                    hash = Mix(hash, Failure(() =>
                    {
                        Marshal.StructureToPtr(new Outer { Tag = 1, Inner = new Inner { X = 2, Y = 3 }, After = 4 }, native, false);
                        hash = Bytes(hash, native, size);
                        var back = Marshal.PtrToStructure<Outer>(native);
                        hash = Mix(Mix(Mix(Mix(hash, back.Tag), back.Inner?.X ?? -1), back.Inner?.Y ?? -1), back.After);
                    }));
                    new Span<byte>((void*)native, 64).Fill(0xAA);
                    hash = Mix(hash, Failure(() =>
                    {
                        Marshal.StructureToPtr(new Outer { Tag = 5, Inner = null, After = 6 }, native, false);
                        hash = Bytes(hash, native, size);
                    }));
                    hash = Mix(hash, Failure(() =>
                    {
                        Marshal.StructureToPtr(new OuterClass { A = 7, Inner = new Inner { X = 8, Y = 9 }, Flag = true }, native, false);
                        hash = Bytes(hash, native, Marshal.SizeOf<OuterClass>());
                        var back = (OuterClass)Marshal.PtrToStructure(native, typeof(OuterClass));
                        hash = Mix(Mix(Mix(Mix(hash, back.A), back.Inner?.X ?? -1), back.Inner?.Y ?? -1), back.Flag ? 1 : 0);
                        Marshal.DestroyStructure<OuterClass>(native);
                    }));
                    return hash;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // ByValArray arrays of structures: copied inline, blittable or converted, at their exact size.
            private static int ArraysOfStructures()
            {
                var hash = Mix(Mix(17, Marshal.SizeOf<Polygon>()), (int)Marshal.OffsetOf<Polygon>("After"));
                hash = Mix(Mix(hash, Marshal.SizeOf<Flags>()), (int)Marshal.OffsetOf<Flags>("After"));
                var native = Native(64);
                try
                {
                    var points = new[] { new Point { X = 1, Y = 2 }, new Point { X = 3, Y = 4 }, new Point { X = 5, Y = 6 } };
                    foreach (var value in new[] { points, points[..2], new Point[4], null })
                    {
                        new Span<byte>((void*)native, 64).Fill(0xAA);
                        hash = Mix(hash, Failure(() =>
                        {
                            Marshal.StructureToPtr(new Polygon { Count = 3, Points = value, After = 7 }, native, false);
                            hash = Bytes(hash, native, Marshal.SizeOf<Polygon>());
                            var back = Marshal.PtrToStructure<Polygon>(native);
                            hash = Mix(Mix(hash, back.Points?.Length ?? -1), back.After);
                            foreach (var point in back.Points ?? []) hash = Mix(Mix(hash, point.X), point.Y);
                        }));
                    }
                    hash = Mix(Mix(hash, Marshal.SizeOf<Shades>()), (int)Marshal.OffsetOf<Shades>("After"));
                    foreach (var shades in new[] { new[] { Shade.Dark, Shade.Light, (Shade)7 }, null })
                    {
                        new Span<byte>((void*)native, 64).Fill(0xAA);
                        hash = Mix(hash, Failure(() =>
                        {
                            Marshal.StructureToPtr(new Shades { Tag = 2, Items = shades, After = 5 }, native, false);
                            hash = Bytes(hash, native, Marshal.SizeOf<Shades>());
                            var back = Marshal.PtrToStructure<Shades>(native);
                            hash = Mix(Mix(Mix(hash, back.Tag), back.After), back.Items.GetType() == typeof(Shade[]) ? 1 : 0);
                            foreach (var shade in back.Items) hash = Mix(hash, (int)shade);
                        }));
                    }
                    new Span<byte>((void*)native, 64).Fill(0xAA);
                    hash = Mix(hash, Failure(() =>
                    {
                        Marshal.StructureToPtr(new Flags { Items = [new Flag { On = true, C = 'x' }, new Flag { C = 'y' }], After = 9 }, native, false);
                        hash = Bytes(hash, native, Marshal.SizeOf<Flags>());
                        var back = Marshal.PtrToStructure<Flags>(native);
                        foreach (var flag in back.Items) hash = Mix(Mix(hash, flag.On ? 1 : 0), flag.C);
                        hash = Mix(hash, back.After);
                    }));
                    return hash;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // SafeHandle fields, in a structure and in a class, in both directions.
            private static int SafeHandles()
            {
                var hash = 17;
                hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.SizeOf<HoldsHandle>())));
                var native = Native(64);
                try
                {
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsHandle { A = 1, H = new Handle(42) }, native, false)));
                    hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.PtrToStructure<HoldsHandle>(native).A)));
                    hash = Mix(hash, Failure(() => Marshal.DestroyStructure<HoldsHandle>(native)));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsHandle { A = 1, H = null }, native, false)));
                    hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.SizeOf<HoldsCritical>())));
                    foreach (var critical in new[] { new Critical(42), null })
                    {
                        new Span<byte>((void*)native, 64).Fill(0xAA);
                        hash = Mix(hash, Failure(() =>
                        {
                            Marshal.StructureToPtr(new HoldsCritical { A = 1, H = critical }, native, false);
                            hash = Mix(Mix(hash, *(int*)native), (long)*(IntPtr*)(native + IntPtr.Size));
                        }));
                    }
                    hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.PtrToStructure<HoldsCritical>(native).A)));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsHandleClass { A = 1, H = new Handle(42) }, native, false)));
                    hash = Mix(hash, Failure(() => hash = Mix(hash, Marshal.PtrToStructure<HoldsHandleClass>(native).A)));
                    return hash;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // A sequential class whose base has automatic layout cannot be marshalled.
            private static int SequentialOnAutoBase()
            {
                var native = Native(64);
                try
                {
                    var hash = Mix(17, Failure(() => Marshal.SizeOf<SequentialOnAuto>()));
                    hash = Mix(hash, Failure(() => Marshal.SizeOf(typeof(SequentialOnAuto))));
                    hash = Mix(hash, Failure(() => Marshal.OffsetOf<SequentialOnAuto>("B")));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new SequentialOnAuto { A = 1, B = 2 }, native, false)));
                    hash = Mix(hash, Failure(() => Marshal.PtrToStructure<SequentialOnAuto>(native)));
                    return Mix(hash, Failure(() => Marshal.PtrToStructure(native, new SequentialOnAuto())));
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // An ANSI ByValArray reads its bytes as UTF-8: a sequence outside ASCII is one character, a byte that is no
            // sequence U+FFFD, and a zero byte a zero character.
            private static int AnsiReads()
            {
                var native = Native(64);
                try
                {
                    var hash = 17;
                    foreach (var bytes in new byte[][]
                    {
                        [0xC3, 0xA9, 0x61, 0x62], [0xFF, 0x61, 0x62, 0x63], [0x61, 0x00, 0x62, 0x63], [0xE4, 0xB8, 0xAD, 0x64],
                    })
                    {
                        bytes.CopyTo(new Span<byte>((void*)native, 4));
                        var back = Marshal.PtrToStructure<AnsiChars>(native);
                        hash = Mix(hash, back.C.Length);
                        foreach (var c in back.C) hash = Mix(hash, c);
                    }
                    return hash;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // ANSI characters outside ASCII, in a ByValArray and alone; ASCII ones round-trip.
            private static int AnsiCharacters()
            {
                var native = Native(64);
                try
                {
                    var hash = Mix(Mix(17, Marshal.SizeOf<AnsiChars>()), Marshal.SizeOf<AnsiChar>());
                    foreach (var text in new[] { "abcd", "abéd", "a中cd", "ab" })
                    {
                        new Span<byte>((void*)native, 64).Fill(0xAA);
                        hash = Mix(hash, Failure(() =>
                        {
                            Marshal.StructureToPtr(new AnsiChars { C = text.ToCharArray(), After = 3 }, native, false);
                            hash = Bytes(hash, native, Marshal.SizeOf<AnsiChars>());
                            var back = Marshal.PtrToStructure<AnsiChars>(native);
                            foreach (var c in back.C) hash = Mix(hash, c);
                            hash = Mix(hash, back.After);
                        }));
                    }
                    foreach (var c in new[] { 'a', 'é', '中' })
                    {
                        new Span<byte>((void*)native, 64).Fill(0xAA);
                        hash = Mix(hash, Failure(() =>
                        {
                            Marshal.StructureToPtr(new AnsiChar { C = c, After = 4 }, native, false);
                            hash = Bytes(hash, native, Marshal.SizeOf<AnsiChar>());
                            var back = Marshal.PtrToStructure<AnsiChar>(native);
                            hash = Mix(Mix(hash, back.C), back.After);
                        }));
                    }
                    return hash;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }
        }
        """;
}
