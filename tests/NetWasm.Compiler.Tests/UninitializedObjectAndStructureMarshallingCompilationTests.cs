using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class UninitializedObjectAndStructureMarshallingCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void UninitializedObjectsMatchDesktop(bool optimize, WasmTarget target) =>
        Assert.Equal(42, Execute("UninitializedObjectFixture", UninitializedSource, optimize, target));

    [Theory]
    [MemberData(nameof(Configurations))]
    public void BlittableStructureMarshallingMatchesDesktop(bool optimize, WasmTarget target) =>
        Assert.Equal(42, Execute("StructureMarshallingFixture", MarshallingSource, optimize, target));

    private static int Execute(string name, string source, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        return executor.Execute(new CompilationScenario(
            $"{name}_{optimize}_{target}", source, $"{name}.EntryPoint", optimize, target, 41, [])
        {
            AllowUnsafe = true,
        });
    }

    internal const string UninitializedSource = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.Serialization;
        namespace UninitializedObjectFixture;

        public class Counted
        {
            public static int Constructed;
            public int Value = 42;
            public string? Name = "x";
            public Counted() { Constructed++; }
            public virtual int Describe() => Value + 1;
        }

        public sealed class Derived : Counted
        {
            public override int Describe() => 7;
        }

        public struct Pair
        {
            public int A;
            public long B;
            public Pair() { A = 5; B = 6; }
        }

        public class WithInitializer
        {
            public static int Runs;
            static WithInitializer() { Runs++; }
            public int Field = 3;
        }

        public class Generic<T>
        {
            public T? Value;
        }

        public class Finalizable
        {
            public int Value = 1;
            ~Finalizable() { }
        }

        public abstract class Abstract { }
        public interface IThing { }
        public delegate void Handler();
        public ref struct RefLike { public int X; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // Storage is zeroed and no instance constructor runs.
                var counted = (Counted)RuntimeHelpers.GetUninitializedObject(typeof(Counted));
                if (Counted.Constructed != 0 || counted.Value != 0 || counted.Name != null) return 1;
                if (counted.Describe() != 1) return 2;
                var derived = (Derived)RuntimeHelpers.GetUninitializedObject(typeof(Derived));
                if (derived.Describe() != 7 || derived.Value != 0) return 3;
                var pair = (Pair)RuntimeHelpers.GetUninitializedObject(typeof(Pair));
                if (pair.A != 0 || pair.B != 0) return 4;
                if (RuntimeHelpers.GetUninitializedObject(typeof(int)) is not int zero || zero != 0) return 5;
                if (RuntimeHelpers.GetUninitializedObject(typeof(int?)) is not int lifted || lifted != 0) return 6;
                if (((Generic<string>)RuntimeHelpers.GetUninitializedObject(typeof(Generic<string>))).Value != null) return 7;
                if (RuntimeHelpers.GetUninitializedObject(typeof(object)).GetType() != typeof(object)) return 8;
                if (((Finalizable)RuntimeHelpers.GetUninitializedObject(typeof(Finalizable))).Value != 0) return 9;

                // The class constructor runs first.
                if (((WithInitializer)RuntimeHelpers.GetUninitializedObject(typeof(WithInitializer))).Field != 0 ||
                    WithInitializer.Runs != 1) return 10;

                // An allocated object's type works through GetType.
                object existing = new Counted();
                if (RuntimeHelpers.GetUninitializedObject(existing.GetType()) is not Counted copy ||
                    copy.Value != 0 || Counted.Constructed != 1) return 11;

        #pragma warning disable SYSLIB0050
                if (FormatterServices.GetUninitializedObject(typeof(Counted)) is not Counted ||
                    FormatterServices.GetSafeUninitializedObject(typeof(Pair)) is not Pair) return 12;
        #pragma warning restore SYSLIB0050

                // Upstream's eligibility checks.
                try { RuntimeHelpers.GetUninitializedObject(null!); return 13; } catch (ArgumentNullException e) { if (e.ParamName != "type") return 14; }
                try { RuntimeHelpers.GetUninitializedObject(typeof(Abstract)); return 15; } catch (MemberAccessException) { }
                try { RuntimeHelpers.GetUninitializedObject(typeof(IThing)); return 16; } catch (MemberAccessException) { }
                try { RuntimeHelpers.GetUninitializedObject(typeof(string)); return 17; } catch (ArgumentException) { }
                try { RuntimeHelpers.GetUninitializedObject(typeof(int[])); return 18; } catch (ArgumentException) { }
        #if DESKTOP_ORACLE
                try { RuntimeHelpers.GetUninitializedObject(typeof(Handler)); return 19; } catch (ArgumentException) { }
        #else
                // Native AOT rejects delegates with MemberAccessException; CoreCLR uses ArgumentException.
                try { RuntimeHelpers.GetUninitializedObject(typeof(Handler)); return 19; } catch (MemberAccessException) { }
        #endif
                try { RuntimeHelpers.GetUninitializedObject(typeof(Generic<>)); return 20; } catch (MemberAccessException) { }
                try { RuntimeHelpers.GetUninitializedObject(typeof(RefLike)); return 21; } catch (NotSupportedException) { }
                return input + 1;
            }
        }
        """;

    internal const string MarshallingSource = """
        using System;
        using System.Runtime.InteropServices;
        namespace StructureMarshallingFixture;

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X; public int Y; }

        public struct Mixed { public byte Tag; public long Value; public short Small; }

        [StructLayout(LayoutKind.Explicit, Size = 12)]
        public struct Overlay
        {
            [FieldOffset(0)] public int Whole;
            [FieldOffset(0)] public short Low;
            [FieldOffset(8)] public float Tail;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Packed { public byte A; public int B; }

        public struct Nested { public Point Origin; public double Scale; public Color Tint; }

        public enum Color : short { Red = 1 }

        public unsafe struct WithPointer { public int* Pointer; public long Value; }

        public unsafe struct Buffer { public fixed byte Bytes[6]; public int After; }

        public struct WithConstructor
        {
            public int X;
            public WithConstructor() { X = 9; }
        }

        public struct WithBool { public bool Flag; public int X; }
        public struct WithChar { public char C; }
        public struct WithString { public string? S; }

        [StructLayout(LayoutKind.Sequential)]
        public class Formatted { public int X; }

        public class Plain { public int X; }

        public struct Generic<T> { public T Value; }

        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                // Sizes and offsets of blittable layouts.
                if (Marshal.SizeOf<Point>() != 8 || Marshal.SizeOf(typeof(Point)) != 8 ||
                    Marshal.SizeOf(new Point()) != 8 || Marshal.SizeOf((object)new Point()) != 8) return 1;
                if (Marshal.SizeOf<Mixed>() != 24 || Marshal.OffsetOf<Mixed>("Value") != (IntPtr)8 ||
                    Marshal.OffsetOf(typeof(Mixed), "Small") != (IntPtr)16) return 2;
                if (Marshal.SizeOf<Overlay>() != 12 || Marshal.OffsetOf<Overlay>("Tail") != (IntPtr)8 ||
                    Marshal.OffsetOf<Overlay>("Low") != IntPtr.Zero) return 3;
                if (Marshal.SizeOf<Packed>() != 5 || Marshal.OffsetOf<Packed>("B") != (IntPtr)1) return 4;
                if (Marshal.SizeOf<Nested>() != 24 || Marshal.OffsetOf<Nested>("Scale") != (IntPtr)8 ||
                    Marshal.OffsetOf<Nested>("Tint") != (IntPtr)16) return 5;
                if (Marshal.SizeOf<int>() != 4 || Marshal.SizeOf<double>() != 8 ||
                    Marshal.SizeOf(typeof(IntPtr)) != IntPtr.Size) return 6;
                try { Marshal.SizeOf<Color>(); return 29; } catch (ArgumentException) { }
                if (Marshal.SizeOf<WithPointer>() != 16 || Marshal.SizeOf<Buffer>() != 12 ||
                    Marshal.OffsetOf<Buffer>("After") != (IntPtr)8) return 7;

                // Round trips through native memory.
                var native = Marshal.AllocHGlobal(64);
                try
                {
                    Marshal.StructureToPtr(new Mixed { Tag = 7, Value = -5, Small = 300 }, native, false);
                    if (Marshal.ReadByte(native) != 7 || Marshal.ReadInt64(native, 8) != -5 ||
                        Marshal.ReadInt16(native, 16) != 300) return 8;
                    var back = Marshal.PtrToStructure<Mixed>(native);
                    if (back.Tag != 7 || back.Value != -5 || back.Small != 300) return 9;
                    if (((Mixed)Marshal.PtrToStructure(native, typeof(Mixed))!).Value != -5) return 10;
                    Marshal.StructureToPtr((object)new Point { X = 3, Y = 4 }, native, fDeleteOld: true);
                    if (Marshal.ReadInt32(native) != 3 || Marshal.ReadInt32(native, 4) != 4) return 11;
                    Marshal.DestroyStructure<Point>(native);
                    Marshal.DestroyStructure(native, typeof(Mixed));
                    Marshal.WriteInt32(native, 77);
                    if (Marshal.PtrToStructure<WithConstructor>(native).X != 77) return 12;
                    if (Marshal.PtrToStructure(IntPtr.Zero, typeof(Point)) != null) return 13;
                    try { _ = Marshal.PtrToStructure<Point>(IntPtr.Zero); return 14; } catch (NullReferenceException) { }
        #if DESKTOP_ORACLE
                    if (((WithConstructor)Marshal.PtrToStructure(native, typeof(WithConstructor))!).X != 77) return 15;
        #else
                    // Zeroed storage would skip the declared constructor, so the Type overload refuses.
                    try { Marshal.PtrToStructure(native, typeof(WithConstructor)); return 15; } catch (PlatformNotSupportedException) { }
        #endif
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }

                // Argument and layout validation.
                try { Marshal.SizeOf(typeof(Generic<int>)); return 16; } catch (ArgumentException) { }
                try { Marshal.SizeOf<Generic<int>>(); return 17; } catch (ArgumentException) { }
                try { Marshal.SizeOf((Type)null!); return 18; } catch (ArgumentNullException) { }
                try { Marshal.SizeOf(typeof(Plain)); return 19; } catch (ArgumentException) { }
                try { Marshal.OffsetOf<Point>("Z"); return 20; } catch (ArgumentException) { }
                try { Marshal.OffsetOf<Point>(null!); return 21; } catch (ArgumentNullException) { }
                // A zero pointer is rejected first (ArgumentNullException); these checks precede any copy.
                var unused = (IntPtr)8;
                try { Marshal.DestroyStructure(IntPtr.Zero, typeof(Point)); return 30; } catch (ArgumentNullException) { }
                try { Marshal.DestroyStructure(unused, typeof(Color)); return 22; } catch (ArgumentException) { }
                try { Marshal.StructureToPtr(new Generic<int>(), unused, false); return 23; } catch (ArgumentException) { }

        #if DESKTOP_ORACLE
                if (Marshal.SizeOf<WithBool>() != 8 || Marshal.SizeOf<WithChar>() != 1 ||
                    Marshal.SizeOf(typeof(WithString)) != IntPtr.Size || Marshal.SizeOf(typeof(Formatted)) != 4) return 24;
        #else
                // Layouts that need field conversion are not available.
                try { Marshal.SizeOf<WithBool>(); return 24; } catch (PlatformNotSupportedException) { }
                try { Marshal.SizeOf<WithChar>(); return 25; } catch (PlatformNotSupportedException) { }
                try { Marshal.SizeOf(typeof(WithString)); return 26; } catch (PlatformNotSupportedException) { }
                try { Marshal.SizeOf(typeof(Formatted)); return 27; } catch (PlatformNotSupportedException) { }
                try { Marshal.StructureToPtr(new WithBool(), unused, false); return 28; } catch (PlatformNotSupportedException) { }
        #endif
                return input + 1;
            }
        }
        """;
}
