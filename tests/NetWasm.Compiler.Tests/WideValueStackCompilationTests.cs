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
