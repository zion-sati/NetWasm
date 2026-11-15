// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

using Xunit;

namespace NetWasm.Compiler.Tests;

public sealed class WideValueStackCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerNormalizesConstructedNativeIntegerStackKinds(
        bool optimize,
        WasmTarget target)
    {
        const string source = """
            using System;

            public static class Program
            {
                public static int Run(int input)
                {
                    return Consume(new IntPtr(input + 40)) +
                        Consume((nint)new UIntPtr((uint)(input + 20)));
                }

                private static int Consume(nint value) => (int)value;
            }
            """;

        Assert.Equal(64, Execute("ConstructedNativeIntegerStack", source, optimize, target));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerOffsetsReferencesWithConstructedNativeIntegers(
        bool optimize,
        WasmTarget target)
    {
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class Program
            {
                public static int Run(int input)
                {
                    var destination = new byte[16];
                    ref byte first = ref MemoryMarshal.GetArrayDataReference(destination);
                    Unsafe.AddByteOffset(ref first, new IntPtr(8)) = (byte)(input + 40);
                    Unsafe.AddByteOffset(ref first, new UIntPtr(4)) = (byte)(input + 20);
                    return destination[8] + destination[4];
                }
            }
            """;

        Assert.Equal(64, Execute("ConstructedNativeIntegerOffset", source, optimize, target));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerWritesUnalignedValuesAtConstructedNativeIntegerOffsets(
        bool optimize,
        WasmTarget target)
    {
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class Program
            {
                public static int Run(int input)
                {
                    var destination = new byte[16];
                    ref byte first = ref MemoryMarshal.GetArrayDataReference(destination);
                    Unsafe.WriteUnaligned(
                        ref Unsafe.AddByteOffset(ref first, new IntPtr(8)),
                        (byte)(input + 40));
                    Unsafe.WriteUnaligned(
                        ref Unsafe.AddByteOffset(ref first, new UIntPtr(4)),
                        (byte)(input + 20));
                    return destination[8] + destination[4];
                }
            }
            """;

        Assert.Equal(64, Execute("ConstructedNativeIntegerWrite", source, optimize, target));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesWideValuesAcrossSpanCallBoundaries(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;

            public static class Program
            {
                public static int Run(int input)
                {
                    var values = new byte[] { 1, 2, 3 };
                    var result = Compose(values, input);
                    return (byte)result;
                }

                private static UInt128 Compose(ReadOnlySpan<byte> values, int input)
                {
                    return (UInt128)(values.Length + input + 39);
                }
            }
            """;

        Assert.Equal(47, Execute("WideValueStack", source, optimize, target, input: 5));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerKeepsAggregateResultsSeparateFromConsumedValues(
        bool optimize,
        WasmTarget target)
    {
        const string source = """
            public struct Pair
            {
                public Pair(int first, int second)
                {
                    First = first;
                    Second = second;
                }

                public int First;
                public int Second;
            }

            public struct Wrapper
            {
                public Wrapper(Pair value)
                {
                    Value = new Pair(value.Second + 30, value.First + 40);
                }

                public Pair Value;
            }

            public abstract class Transformer
            {
                public abstract Pair Apply(Pair value);
            }

            public sealed class DerivedTransformer : Transformer
            {
                public override Pair Apply(Pair value) =>
                    new Pair(value.First + 10, value.Second + 20);
            }

            public delegate Pair PairTransform(Pair value);

            public static class Program
            {
                public static int Run(int input)
                {
                    var value = new Pair(input + 1, input + 2);
                    var direct = Transform(value);
                    Transformer transformer = new DerivedTransformer();
                    var dispatched = transformer.Apply(value);
                    PairTransform callback = Transform;
                    var delegated = callback(value);
                    var wrapped = new Wrapper(value);
                    return direct.First + direct.Second +
                        dispatched.First + dispatched.Second +
                        delegated.First + delegated.Second +
                        wrapped.Value.First + wrapped.Value.Second;
                }

                private static Pair Transform(Pair value) =>
                    new Pair(value.Second + 1, value.First + 2);
            }
            """;

        Assert.Equal(158, Execute(
            "AggregateResultAliasing",
            source,
            optimize,
            target,
            input: 5));
    }

    private static int Execute(
        string name,
        string source,
        bool optimize,
        WasmTarget target,
        int input = 2)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        return executor.Execute(new CompilationScenario(
            name,
            source,
            "Program",
            optimize,
            target,
            input,
            ImmutableArray<string>.Empty));
    }
}
