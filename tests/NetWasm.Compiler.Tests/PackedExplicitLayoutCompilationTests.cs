// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PackedExplicitLayoutCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ExplicitManagedByReferenceAliasesReferToTheSameStorage(bool optimize, WasmTarget target)
    {
        const string source = """
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)]
            public ref struct Union
            {
                [FieldOffset(0)] public ref int First;
                [FieldOffset(0)] public ref int Second;
                public Union(ref int value) { this = default; First = ref value; }
            }
            public ref struct Nested { public Union Value; }
            public static class Program
            {
                public static int Run(int input)
                {
                    var nested = new Nested { Value = new Union(ref input) };
                    nested.Value.Second++;
                    return input;
                }
            }
            """;
        Assert.Equal(42, Execute(source, optimize, target));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RejectsOverlappingObjectAndManagedByReferenceFields(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)]
            public ref struct Invalid
            {
                [FieldOffset(0)] public ref int Address;
                [FieldOffset(0)] public object Reference;
            }
            public static class Program
            {
                public static unsafe int Run(int input) => sizeof(Invalid);
            }
            """;
        var assembly = assets.CompileUnsafeSource("InvalidMixedReferenceUnion", source);
        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new(assembly, [assets.CoreLib], "Program", "Run", [], target)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData(1, WasmTarget.Wasm32)]
    [InlineData(1, WasmTarget.Wasm64)]
    [InlineData(0, WasmTarget.Wasm32)]
    [InlineData(0, WasmTarget.Wasm64)]
    public void RejectsUnsafeReferenceUnionMetadataBeforeProducingAModule(int referenceOffset, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)]
            public struct InvalidUnion
            {
                [FieldOffset({{referenceOffset}})] public object Reference;
                [FieldOffset(0)] public int Number;
            }
            public static class Program
            {
                public static int Run(int input)
                {
                    var value = new InvalidUnion { Number = input };
                    return value.Number;
                }
            }
            """;
        var assembly = assets.CompileSource("InvalidReferenceUnion", source);
        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new(assembly, [assets.CoreLib], "Program", "Run", [], target)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PackedSequentialValuesPreserveOffsetsCopiesArraysBoxingAndPointers(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct Packed { public byte Tag; public int Number; public short Tail; }
            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            public struct PackedTwo { public byte Tag; public int Number; public short Tail; }
            [StructLayout(LayoutKind.Sequential, Size = 9)]
            public struct DeclaredNine { public int Number; }
            [StructLayout(LayoutKind.Sequential, Size = 1)]
            public struct DeclaredSmall { public int Number; }
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct Nested<T> { public byte Tag; public T Value; }
            [InlineArray(2)] public struct Inline { private Packed _first; }
            public static class Program
            {
                private static T Copy<T>(T value) => value;
                public static unsafe int Run(int input)
                {
                    if (sizeof(Packed) != 7 || sizeof(PackedTwo) != 8) return 1;
                    if (sizeof(DeclaredNine) != 9 || sizeof(DeclaredSmall) != 4) return 8;
                    var value = new Packed { Tag = 3, Number = input, Tail = 9 };
                    if ((byte*)&value.Number - (byte*)&value != 1) return 2;
                    var two = new PackedTwo { Number = input };
                    if ((byte*)&two.Number - (byte*)&two != 2) return 3;
                    if (*(int*)((byte*)&value + 1) != input) return 4;
                    *(int*)((byte*)&value + 1) = input + 1;
                    var copied = Copy(value);
                    object boxed = copied;
                    var values = new Packed[] { value, (Packed)boxed };
                    var clone = new Packed[2];
                    Array.Copy(values, clone, 2);
                    if (clone[1].Number != input + 1 || clone[1].Tail != 9) return 5;
                    var nested = new Nested<Packed> { Tag = 7, Value = copied };
                    if (Unsafe.SizeOf<Nested<Packed>>() != 8 || nested.Value.Number != input + 1) return 6;
                    Inline inline = default;
                    inline[1] = copied;
                    if (Unsafe.SizeOf<Inline>() != 14 || inline[1].Number != input + 1) return 7;
                    return 42;
                }
            }
            """;
        Assert.Equal(42, Execute(source, optimize, target));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ExplicitUnionsPreserveAliasesCopiesArraysBoxingAndPointers(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)]
            public struct Union
            {
                [FieldOffset(0)] public int Integer;
                [FieldOffset(0)] public float Single;
                [FieldOffset(0)] public byte Low;
                [FieldOffset(8)] public long Tail;
            }
            [StructLayout(LayoutKind.Explicit, Size = 9)]
            public struct OddSize { [FieldOffset(0)] public int Number; }
            [StructLayout(LayoutKind.Explicit, Size = 1)]
            public struct Misaligned { [FieldOffset(1)] public int Number; }
            public struct Nested<T> { public T Value; public int Tail; }
            public static class Program
            {
                private static T Copy<T>(T value) => value;
                public static unsafe int Run(int input)
                {
                    if (sizeof(Union) != 16) return 1;
                    if (sizeof(OddSize) != 9 || sizeof(Misaligned) != 5) return 8;
                    var value = new Union { Single = 1.0f, Tail = input };
                    if (value.Integer != 1065353216) return 2;
                    value.Low = 1;
                    if (value.Integer != 1065353217) return 3;
                    if ((byte*)&value.Single - (byte*)&value.Integer != 0) return 4;
                    *(int*)&value = input;
                    if (value.Integer != input) return 5;
                    object boxed = Copy(value);
                    var values = new Union[] { value, (Union)boxed };
                    var clone = new Union[2];
                    Array.Copy(values, clone, 2);
                    if (clone[1].Integer != input || clone[1].Tail != input) return 6;
                    var nested = new Nested<Union> { Value = Copy(clone[1]), Tail = 9 };
                    if (nested.Value.Integer != input || nested.Tail != 9) return 7;
                    return 42;
                }
            }
            """;
        Assert.Equal(42, Execute(source, optimize, target));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ExplicitReferenceAliasesAndPackedReferencesSurviveCopies(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)]
            public struct ReferenceUnion
            {
                [FieldOffset(0)] public object First;
                [FieldOffset(0)] public object Second;
                [FieldOffset(8)] public int Tag;
            }
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct PackedReference { public byte Tag; public ReferenceUnion Value; }
            public sealed class Value { public int Number; }
            public static class Program
            {
                public static int Run(int input)
                {
                    var marker = new Value { Number = input };
                    var union = new ReferenceUnion { First = marker, Tag = 9 };
                    if (!object.ReferenceEquals(union.Second, marker)) return 1;
                    var packed = new PackedReference { Tag = 3, Value = union };
                    var values = new PackedReference[] { packed, packed };
                    object boxed = values[1];
                    var copied = (PackedReference)boxed;
                    GC.Collect();
                    if (((Value)copied.Value.Second).Number != input || copied.Value.Tag != 9) return 2;
                    copied.Value.Second = new Value { Number = input + 1 };
                    if (((Value)copied.Value.First).Number != input + 1) return 3;
                    return 42;
                }
            }
            """;
        Assert.Equal(42, Execute(source, optimize, target));
    }

    private static int Execute(string source, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        return executor.Execute(new("LayoutFixture", source, "Program", optimize, target, 41, [])
        {
            AllowUnsafe = true,
        });
    }
}
