// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Marshal's structure members on formatted classes and on structures with string fields, as desktop .NET's runtime
// marshalling converts them on Unix. A sequential or explicit class lays out its base classes' fields first (a private
// base field is laid out but OffsetOf does not find it); PtrToStructure fills a new object, created with its
// parameterless constructor, or an existing one; a class without layout, a structure field desktop rejects (an
// object, an array without ByValArray, a StringBuilder) and a value passed to the existing-object overload throw what
// desktop throws. Strings are UTF-8 (ANSI, LPStr, LPUTF8Str) or UTF-16 (LPWStr, CharSet.Unicode) CoTaskMem copies,
// null as a null pointer, which DestroyStructure frees, or inline (ByValTStr: truncated to SizeConst - 1 characters,
// then terminated; read back up to the terminator). Values that depend on the pointer size are compared as
// conditions, so 64-bit desktop and wasm32 agree.
public sealed class FormattedClassAndStringStructureMarshallingCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FormattedClassesAndStringFieldsMatchDesktop() =>
        runner.Run(new("FormattedClassAndStringMarshalling", "NetWasm.Correctness.FormattedClasses", Source, [0, 1, 7])
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
        using System.Text;

        namespace NetWasm.Correctness.FormattedClasses;

        [StructLayout(LayoutKind.Sequential)] public class Blittable { public int A; public long B; public byte C; }
        [StructLayout(LayoutKind.Sequential)] public class Flagged { public int A; public bool B; public char C; }
        [StructLayout(LayoutKind.Sequential)] public class Base { public int X; public bool F; private short hidden = 7; public short Hidden => hidden; }
        [StructLayout(LayoutKind.Sequential)] public class Derived : Base { public char Y; public long Z; }
        [StructLayout(LayoutKind.Explicit)] public class Overlaid { [FieldOffset(0)] public int I; [FieldOffset(4)] public bool B; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public class Packed { public byte X; public bool B; public long L; }
        [StructLayout(LayoutKind.Sequential)] public class NoParameterless { public int A; public NoParameterless(int a) { A = a; } }
        [StructLayout(LayoutKind.Sequential)] public class Initialized { public int A = 99; public int B; }
        public class Automatic { public int Q; }

        public struct Utf8Text { public int A; public string Name; }
        public struct WideText { public int A; [MarshalAs(UnmanagedType.LPWStr)] public string Name; }
        public struct ExplicitUtf8 { public int A; [MarshalAs(UnmanagedType.LPUTF8Str)] public string Name; }
        public struct AnsiText { public int A; [MarshalAs(UnmanagedType.LPStr)] public string Name; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct UnicodeText { public int A; public string Name; }
        public struct InlineText { public int A; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string Name; public int B; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct InlineWideText { public int A; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string Name; public int B; }
        [StructLayout(LayoutKind.Sequential)] public class TextClass { public int A; public string Name; }
        public struct HoldsObject { public int A; public object O; }
        public struct HoldsArray { public int A; public int[] Items; }
        public struct HoldsBuilder { public int A; public StringBuilder B; }
        public struct HoldsDelegate { public int A; public Action D; }

        public static unsafe class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value + (int)(value >> 32));

            private static int Mix(int hash, string text)
            {
                if (text is null) return Mix(hash, -1);
                foreach (var c in text) hash = Mix(hash, c);
                return Mix(hash, text.Length);
            }

            private static int Failure(Action action)
            {
                try { action(); return 0; }
                catch (MissingMethodException) { return 3; }
                catch (TypeLoadException) { return 2; }
                catch (ArgumentException) { return 1; }
                catch (PlatformNotSupportedException) { return 9; }
            }

            private static IntPtr Native(int size)
            {
                var native = Marshal.AllocHGlobal(size);
                new Span<byte>((void*)native, size).Fill(0xAA);
                return native;
            }

            // A formatted class's native bytes and the object read back from them, through each PtrToStructure overload.
            private static T ClassBytes<T>(ref int hash, T value) where T : class
            {
                var size = Marshal.SizeOf<T>();
                var native = Native(size);
                try
                {
                    Marshal.StructureToPtr(value, native, false);
                    hash = Mix(hash, size);
                    foreach (var b in new ReadOnlySpan<byte>((void*)native, size)) hash = Mix(hash, b);
                    var created = (T)Marshal.PtrToStructure(native, typeof(T));
                    var existing = Marshal.PtrToStructure<T>(native);
                    Marshal.PtrToStructure(native, (object)existing);
                    Marshal.DestroyStructure<T>(native);
                    hash = Mix(hash, ReferenceEquals(created, existing) ? 1 : 2);
                    return existing;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // A structure's string field: what the native pointer reads as, and the value read back.
            private static int Text<T>(int hash, T value, int offset, bool wide, Func<T, string> name) where T : struct
            {
                var native = Native(Marshal.SizeOf<T>());
                try
                {
                    Marshal.StructureToPtr(value, native, false);
                    var pointer = *(IntPtr*)(native + offset);
                    hash = Mix(hash, pointer == IntPtr.Zero ? null : wide ? Marshal.PtrToStringUni(pointer) : Marshal.PtrToStringUTF8(pointer));
                    hash = Mix(hash, name(Marshal.PtrToStructure<T>(native)));
                    // Replacing the old value frees its strings first.
                    Marshal.StructureToPtr(value, native, true);
                    Marshal.DestroyStructure<T>(native);
                    return hash;
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            // An inline string's native bytes (no pointers) and the value read back.
            private static int Inline<T>(int hash, T value, Func<T, string> name) where T : struct
            {
                var size = Marshal.SizeOf<T>();
                var native = Native(size);
                try
                {
                    Marshal.StructureToPtr(value, native, false);
                    foreach (var b in new ReadOnlySpan<byte>((void*)native, size)) hash = Mix(hash, b);
                    hash = Mix(hash, name(Marshal.PtrToStructure<T>(native)));
                    Marshal.DestroyStructure<T>(native);
                    return Mix(hash, size);
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            }

            public static int Run(int input)
            {
                var hash = 17;
                var word = input == 1 ? "héllo" : "plain" + input;

                // Formatted classes.
                var blittable = ClassBytes(ref hash, new Blittable { A = input, B = 0x0102030405060708, C = 9 });
                hash = Mix(hash, blittable.A + blittable.B + blittable.C);
                var flagged = ClassBytes(ref hash, new Flagged { A = 2, B = input != 0, C = input == 1 ? 'é' : 'q' });
                hash = Mix(hash, flagged.A + (flagged.B ? 10 : 0) + flagged.C * 100);
                var derived = ClassBytes(ref hash, new Derived { X = 3, F = true, Y = 'Q', Z = 77 + input });
                hash = Mix(hash, derived.X + (derived.F ? 10 : 0) + derived.Hidden * 100 + derived.Y * 1000 + derived.Z * 100000);
                var overlaid = ClassBytes(ref hash, new Overlaid { I = 0x11223344 + input, B = true });
                hash = Mix(hash, overlaid.I + (overlaid.B ? 1 : 0));
                var packed = ClassBytes(ref hash, new Packed { X = 1, B = true, L = 2 + input });
                hash = Mix(hash, packed.X + (packed.B ? 10 : 0) + packed.L * 100);
                foreach (var size in new[] { Marshal.SizeOf<Blittable>(), Marshal.SizeOf<Flagged>(), Marshal.SizeOf<Base>(),
                    Marshal.SizeOf<Derived>(), Marshal.SizeOf<Overlaid>(), Marshal.SizeOf<Packed>() }) hash = Mix(hash, size);
                foreach (var name in new[] { "X", "F", "Y", "Z" }) hash = Mix(hash, (long)Marshal.OffsetOf<Derived>(name));
                hash = Mix(hash, Failure(() => Marshal.OffsetOf<Derived>("hidden")));

                var native = Native(64);
                try
                {
                    *(int*)native = 5;
                    *((int*)native + 1) = 6;
                    var initialized = Marshal.PtrToStructure<Initialized>(native);
                    hash = Mix(hash, initialized.A * 10 + initialized.B);
                    hash = Mix(hash, Failure(() => Marshal.PtrToStructure(native, typeof(NoParameterless))));
                    hash = Mix(hash, Failure(() => Marshal.PtrToStructure<NoParameterless>(native)));
                    hash = Mix(hash, Failure(() => Marshal.SizeOf<Automatic>()));
                    hash = Mix(hash, Failure(() => Marshal.SizeOf((object)new Automatic())));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new Automatic(), native, false)));
                    hash = Mix(hash, Failure(() => Marshal.PtrToStructure<Automatic>(native)));
                    hash = Mix(hash, Failure(() => Marshal.DestroyStructure<Automatic>(native)));
                    hash = Mix(hash, Failure(() => Marshal.PtrToStructure(native, (object)new Utf8Text())));

                    // Fields desktop rejects, and a null delegate.
                    hash = Mix(hash, Failure(() => Marshal.SizeOf<HoldsObject>()));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsObject { A = 1, O = 5 }, native, false)));
                    hash = Mix(hash, Failure(() => Marshal.SizeOf<HoldsArray>()));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsArray { A = 1, Items = new[] { 1 } }, native, false)));
                    hash = Mix(hash, Failure(() => Marshal.SizeOf<HoldsBuilder>()));
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsBuilder { A = 1, B = new StringBuilder("x") }, native, false)));
                    hash = Mix(hash, Marshal.SizeOf<HoldsDelegate>() == (IntPtr.Size == 8 ? 16 : 8) ? 1 : 2);
                    hash = Mix(hash, Failure(() => Marshal.StructureToPtr(new HoldsDelegate { A = 1 }, native, false)));
                    hash = Mix(hash, Marshal.PtrToStructure<HoldsDelegate>(native).D is null ? 1 : 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }

                // String fields.
                var pointerField = IntPtr.Size == 8 ? 8 : 4;
                hash = Mix(hash, Marshal.SizeOf<Utf8Text>() == 2 * pointerField ? 1 : 2);
                hash = Mix(hash, (long)Marshal.OffsetOf<Utf8Text>("Name") == pointerField ? 1 : 2);
                hash = Text(hash, new Utf8Text { A = 1, Name = word }, pointerField, false, value => value.Name);
                hash = Text(hash, new Utf8Text { A = 2, Name = null }, pointerField, false, value => value.Name);
                hash = Text(hash, new WideText { A = 3, Name = "wïde" + input }, pointerField, true, value => value.Name);
                hash = Text(hash, new ExplicitUtf8 { A = 4, Name = "ütf8" }, pointerField, false, value => value.Name);
                hash = Text(hash, new AnsiText { A = 5, Name = "lpstr€" }, pointerField, false, value => value.Name);
                hash = Text(hash, new UnicodeText { A = 6, Name = "uni" + input }, pointerField, true, value => value.Name);
                hash = Inline(hash, new InlineText { A = 7, Name = "abcdefghijk", B = 8 }, value => value.Name + "/" + value.B);
                hash = Inline(hash, new InlineText { A = 7, Name = input == 1 ? "hé" : "hi", B = 8 }, value => value.Name);
                hash = Inline(hash, new InlineText { A = 7, Name = null, B = 8 }, value => value.Name);
                hash = Inline(hash, new InlineWideText { A = 7, Name = "abcdefghijk", B = 8 }, value => value.Name + "/" + value.B);
                var textClass = Native(Marshal.SizeOf<TextClass>());
                try
                {
                    Marshal.StructureToPtr(new TextClass { A = 9, Name = word }, textClass, false);
                    hash = Mix(hash, Marshal.PtrToStringUTF8(*(IntPtr*)(textClass + pointerField)));
                    var back = Marshal.PtrToStructure<TextClass>(textClass);
                    hash = Mix(hash, back.A);
                    hash = Mix(hash, back.Name);
                    Marshal.DestroyStructure<TextClass>(textClass);
                }
                finally
                {
                    Marshal.FreeHGlobal(textClass);
                }

                _trace = hash;
                return hash;
            }
        }
        """;
}
