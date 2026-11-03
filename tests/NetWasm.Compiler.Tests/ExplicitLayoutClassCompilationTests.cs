using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Legality;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A class with explicit layout places its fields at their FieldOffset, after
// its base's fields, so fields that share bytes see each other's stores: a
// reread after a store to an overlapping field, stores on two arms, an object
// that never escapes, a loop that the unroller flattens, and a derived class
// whose reference survives a collection. Every preset gives desktop .NET's
// results, and the layouts CoreCLR rejects when it loads the type, among them
// any generic type's, are compile-time diagnostics.
public sealed class ExplicitLayoutClassCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            [StructLayout(LayoutKind.Explicit)]
            private sealed class Overlay { [FieldOffset(0)] public int Whole; [FieldOffset(0)] public short Low; [FieldOffset(2)] public short High; }

            [StructLayout(LayoutKind.Sequential)]
            private class Base { public int Before = 11; }

            [StructLayout(LayoutKind.Explicit)]
            private sealed class Derived : Base
            {
                [FieldOffset(0)] public int Bits;
                [FieldOffset(0)] public float Value;
                [FieldOffset(12)] public string? Name;
            }

            // Offsets count from the base's fields; the reference survives a collection.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Inherited(int input)
            {
                var derived = new Derived { Value = input + 0.5f, Name = new string('n', input + 3) };
                GC.Collect();
                return derived.Bits % 100000 + derived.Before * 10 + derived.Name!.Length;
            }

            // A read of one field after a store to a field that overlaps it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Reread(Overlay overlay, int input)
            {
                var a = overlay.Whole;
                overlay.Low = (short)(input + 2);
                return a * 1000 + overlay.Whole;
            }

            // Both arms store one field; one also stores an overlapping field after it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArmStores(Overlay overlay, int input)
            {
                if (input % 2 == 0) { overlay.Whole = input * 3 + 0x50000; overlay.High = 7; }
                else overlay.Whole = input * 5 + 0x60000;
                return overlay.Whole;
            }

            // An object that never escapes, written and read through overlapping fields.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Local(int input)
            {
                var overlay = new Overlay { Whole = 0x30000 + input };
                overlay.High = (short)(input + 9);
                return overlay.Whole + overlay.Low;
            }

            // A loop that reads one field while it writes an overlapping one.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Loop(Overlay overlay, int input)
            {
                var total = 0;
                for (var i = 0; i < 4; i++)
                {
                    total = total * 7 + overlay.Whole % 1000;
                    overlay.Low = (short)(i + input);
                }
                return total;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                return which switch
                {
                    0 => Reread(new Overlay { Whole = 0x10000 * (input + 1) }, input),
                    1 => ArmStores(new Overlay(), input),
                    2 => Local(input),
                    3 => Loop(new Overlay { Whole = 0x20000 }, input),
                    4 => Inherited(input),
                    _ => -1,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 65601538), (1, 131203075), (6, 459210760), (100, 458752), (101, 393221), (106, 458770), (200, 589824), (201, 655362), (206, 983052), (300, 28809), (301, 28866), (306, 29151), (400, 64721), (401, 47634), (406, 73431)];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O0)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void OverlappingFieldsShareTheirBytes(FinalWasmOptimization optimization) =>
        LegalityCases.Run("ExplicitLayoutClass" + optimization, Source, Cases, optimization);

    [Theory]
    [InlineData("""
        [StructLayout(LayoutKind.Explicit)]
        public sealed class Invalid { [FieldOffset(0)] public string? Name; [FieldOffset(0)] public int Bits; }
        """, "explicit reference overlaps incompatible storage")]
    [InlineData("""
        public class Base { public int Before; }
        [StructLayout(LayoutKind.Explicit)]
        public sealed class Invalid : Base { [FieldOffset(0)] public int Bits; }
        """, "explicit layout over the automatic layout of")]
    public void LayoutsCoreClrRejectsAreDiagnostics(string type, string reason)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("InvalidExplicitLayout" + reason.Length, $$"""
            using System.Runtime.InteropServices;
            {{type}}
            public static class EntryPoint
            {
                public static int Run(int input) => new Invalid { Bits = input }.Bits;
            }
            """);
        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains(reason, exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    // "Generic types cannot have explicit layout": a generic class or struct, or
    // a class nested in a generic type, which shares its type parameters.
    [Theory]
    [InlineData("""
        [StructLayout(LayoutKind.Explicit)]
        public sealed class Invalid<T> { [FieldOffset(0)] public int Bits; [FieldOffset(0)] public uint Same; }
        """, "new Invalid<int> { Bits = input }.Same")]
    [InlineData("""
        [StructLayout(LayoutKind.Explicit)]
        public struct Invalid<T> { [FieldOffset(0)] public int Bits; [FieldOffset(0)] public uint Same; }
        """, "new Invalid<int> { Bits = input }.Same")]
    [InlineData("""
        public static class Outer<T>
        {
            [StructLayout(LayoutKind.Explicit)]
            public sealed class Invalid { [FieldOffset(0)] public int Bits; [FieldOffset(0)] public uint Same; }
        }
        """, "new Outer<int>.Invalid { Bits = input }.Same")]
    public void GenericTypesWithExplicitLayoutAreDiagnostics(string type, string use)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("GenericExplicitLayout" + use.Length + type.Length, $$"""
            using System.Runtime.InteropServices;
            {{type}}
            public static class EntryPoint
            {
                public static int Run(int input) => (int){{use}};
            }
            """);
        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("generic explicit layout", exception.Diagnostic.Message, StringComparison.Ordinal);
    }
}
