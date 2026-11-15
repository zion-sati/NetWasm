// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class UnsafeCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void UnsafeAddPreservesEveryOffsetOverload(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace UnsafeOffsetFixture;
            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    ulong[] values = [11, 10000000000000000000UL, 33];
                    ref var first = ref values[0];
                    var offset = input - 40;
                    var nativeOffset = (nint)offset;
                    var unsignedOffset = (nuint)offset;
                    if (Unsafe.Add(ref first, offset) != values[1]) return 1;
                    if (Unsafe.Add(ref first, nativeOffset) != values[1]) return 2;
                    if (Unsafe.Add(ref first, unsignedOffset) != values[1]) return 3;
                    ref var second = ref values[1];
                    if (Unsafe.Add(ref second, -offset) != values[0]) return 4;
                    if (Unsafe.Add(ref second, -nativeOffset) != values[0]) return 5;
                    if (Unsafe.Add(ref second, unchecked((nuint)(-nativeOffset))) != values[0]) return 6;
                    fixed (ulong* pointer = values)
                    {
                        if (*(ulong*)Unsafe.Add<ulong>(pointer, offset) != values[1]) return 7;
                        if (*(ulong*)Unsafe.Add<ulong>(pointer + 1, -offset) != values[0]) return 8;
                    }
                    // Compare addresses only: the large offset must not be truncated on wasm64.
                    var large = unchecked((nint)(0x100000001UL + (ulong)offset));
                    if (Unsafe.ByteOffset(ref first, ref Unsafe.Add(ref first, large)) != large * sizeof(ulong)) return 9;
                    if (Unsafe.ByteOffset(ref first, ref Unsafe.Add(ref first, (nuint)large)) != large * sizeof(ulong)) return 10;
                    ReadOnlySpan<ulong> table = [0, 10000000000000000000UL];
                    if (Unsafe.Add(ref MemoryMarshal.GetReference(table), nativeOffset) != table[1]) return 11;
                    return 0;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedUnsafeSource("UnsafeOffsetFixture", source)
            : assets.CompileUnsafeSource("UnsafeOffsetFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "UnsafeOffsetFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(0, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void CompilerLowersUnsafeReferenceComparisons()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UnsafeReferenceComparisonFixture",
            """
            namespace UnsafeReferenceComparisonFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = input;
                    var second = 1;
                    return
                        (System.Runtime.CompilerServices.Unsafe.AreSame(in first, in first) ? 1 : 0) +
                        (System.Runtime.CompilerServices.Unsafe.IsAddressLessThan(in first, in second) ? 2 : 0) +
                        (System.Runtime.CompilerServices.Unsafe.IsAddressGreaterThanOrEqualTo(in second, in first) ? 4 : 0) +
                        (System.Runtime.CompilerServices.Unsafe.IsNullRef(in System.Runtime.CompilerServices.Unsafe.NullRef<int>()) ? 8 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnsafeReferenceComparisonFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(15, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersMemoryMarshalGetReferenceForMutableAndReadOnlySpans()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "MemoryMarshalReferenceFixture",
            """
            namespace MemoryMarshalReferenceFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[] { input, 1 };
                    var span = new System.Span<int>(values);
                    ref int first = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span);
                    first++;

                    System.ReadOnlySpan<int> readOnly = values;
                    ref readonly int firstReadOnly = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(readOnly);
                    var scalar = input;
                    var created = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in scalar, 1);
                    return firstReadOnly + created[0] - input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "MemoryMarshalReferenceFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersUnsafeSizeByteOffsetAndSpanOverlap()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UnsafeSpanOverlapFixture",
            """
            namespace UnsafeSpanOverlapFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[] { input, 1, 2 };
                    System.ReadOnlySpan<int> first = new(values, 0, 2);
                    System.ReadOnlySpan<int> second = new(values, 1, 2);
                    var overlaps = System.MemoryExtensions.Overlaps(
                        first,
                        second,
                        out var elementOffset);
                    return input +
                        System.Runtime.CompilerServices.Unsafe.SizeOf<int>() +
                        elementOffset +
                        (overlaps ? 1 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnsafeSpanOverlapFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(47, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersReadOnlySpanCovariantCast()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ReadOnlySpanCastFixture",
            """
            namespace ReadOnlySpanCastFixture;

            public class Base
            {
                public int Value;
            }

            public sealed class Derived : Base
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    System.ReadOnlySpan<Derived> derived =
                        new Derived[] { new() { Value = input } };
                    var values = System.ReadOnlySpan<Base>.CastUp(derived);
                    return values[0].Value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ReadOnlySpanCastFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesUnsafeMemorySurface()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "UnsafeMemorySurfaceFixture",
            """
            namespace UnsafeMemorySurfaceFixture;

            public class Base
            {
                public int Value;
            }

            public sealed class Derived : Base
            {
            }

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[] { input, 2, 3 };
                    ref int first = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(values);
                    ref int second = ref System.Runtime.CompilerServices.Unsafe.Add(ref first, 1);
                    ref int third = ref System.Runtime.CompilerServices.Unsafe.AddByteOffset(
                        ref first,
                        (nint)8);
                    ref int back = ref System.Runtime.CompilerServices.Unsafe.Subtract(ref second, 1);

                    var source = stackalloc byte[4];
                    var destination = stackalloc byte[4];
                    System.Runtime.CompilerServices.Unsafe.InitBlock(source, 7, 4);
                    System.Runtime.CompilerServices.Unsafe.CopyBlock(destination, source, 4);
                    System.Runtime.CompilerServices.Unsafe.Write(destination, input);
                    var copied = System.Runtime.CompilerServices.Unsafe.Read<int>(destination);

                    var bits = System.Runtime.CompilerServices.Unsafe.BitCast<float, int>(
                        System.Runtime.CompilerServices.Unsafe.BitCast<int, float>(input));
                    object boxedReference = new Derived { Value = input };
                    var derived = System.Runtime.CompilerServices.Unsafe.As<Derived>(boxedReference);
                    object boxedValue = input;
                    ref int unboxed = ref System.Runtime.CompilerServices.Unsafe.Unbox<int>(boxedValue);
                    unboxed++;
                    System.Runtime.CompilerServices.Unsafe.SkipInit(out int skipped);

                    return second == 2 && third == 3 && back == input && copied == input
                        && bits == input && derived.Value == input && unboxed == input + 1
                        && skipped == 0
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnsafeMemorySurfaceFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersPointerReadsWritesAndSizeOf()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "UnsafePointerFixture",
            """
            namespace UnsafePointerFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    var pointer = &value;
                    *pointer += 1;
                    return *pointer + sizeof(int);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnsafePointerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(46, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersStackAllocationAndBlockInitialization()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "StackAllocationFixture",
            """
            namespace StackAllocationFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = stackalloc int[2];
                    values[0] = input;
                    values[1] = 1;
                    return values[0] + values[1];
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StackAllocationFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerKeepsFixedArrayAliveForInteriorPointer()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "FixedPointerFixture",
            """
            namespace FixedPointerFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[] { input, 1 };
                    fixed (int* pointer = values)
                    {
                        _ = new object();
                        return pointer[0] + pointer[1];
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FixedPointerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StackAllocationUsesTargetNativeSizeSemantics(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "StackAllocationOverflowFixture",
            """
            namespace StackAllocationOverflowFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = stackalloc int[int.MaxValue];
                    values[0] = input;
                    return values[0];
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StackAllocationOverflowFixture.EntryPoint",
            "Run",
            [],
            target));

        Assert.Equal(target, result.Layouts.Target.Target);
        if (target == WasmTarget.Wasm32)
        {
            var exception = Assert.ThrowsAny<Exception>((Action)(() =>
                ExecuteWithStandardWasiNode(
                    result.ApplicationModule,
                    assets.Directory,
                    41,
                    target,
                    result.StaticDataEnd)));
            Assert.Contains("exception", exception.ToString());
        }
        else
        {
            Assert.Equal(41, ExecuteWithStandardWasiNode(
                result.ApplicationModule,
                assets.Directory,
                41,
                target,
                result.StaticDataEnd));
        }
    }

    [Fact]
    public void StackAllocationSupportsMaximumNaturalAlignment()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "StackAllocationAlignmentFixture",
            """
            namespace StackAllocationAlignmentFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = stackalloc long[1];
                    return ((nuint)values & 15) == 0 ? input + 1 : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StackAllocationAlignmentFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerUsesTargetWidthForPointers()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "PointerWidthFixture",
            """
            namespace PointerWidthFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    int* pointer = &value;
                    return *pointer;
                }
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "PointerWidthFixture.EntryPoint",
                "Run",
                [],
                target));

            Assert.Equal(target, result.Layouts.Target.Target);
        }
    }

    [Fact]
    public void CompilerLowersStaticFunctionPointers()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            namespace FunctionPointerFixture;

            public static unsafe class EntryPoint
            {
                private static int Increment(int value) => value + 1;

                public static int Run(int input)
                {
                    delegate* managed<int, int> operation = &Increment;
                    return operation(input);
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = executor.Execute(new CompilationScenario(
                "FunctionPointerFixture",
                source,
                "FunctionPointerFixture.EntryPoint",
                Optimize: false,
                target,
                41,
                [])
            {
                AllowUnsafe = true,
            });

            Assert.Equal(42, result);
        }
    }

    [Fact]
    public void CompilerLowersFunctionPointersWithMultipleScalarSignatures()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            namespace FunctionPointerSignatureFixture;

            public static unsafe class EntryPoint
            {
                private static long Combine(int left, long right) => left + right;
                private static void Store(int* destination, int value) => *destination = value;

                public static int Run(int input)
                {
                    delegate* managed<int, long, long> combine = &Combine;
                    delegate* managed<int*, int, void> store = &Store;
                    var result = (int)combine(input, 1L);
                    store(&result, result);
                    return result;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = executor.Execute(new CompilationScenario(
                "FunctionPointerSignatureFixture",
                source,
                "FunctionPointerSignatureFixture.EntryPoint",
                Optimize: false,
                target,
                41,
                [])
            {
                AllowUnsafe = true,
            });

            Assert.Equal(42, result);
        }
    }
}
