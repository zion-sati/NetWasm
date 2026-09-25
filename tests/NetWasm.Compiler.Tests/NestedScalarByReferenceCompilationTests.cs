using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NestedScalarByReferenceCompilationTests
{
    private const string Source = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                uint first = 1;
                uint second = 0;
                Forward((byte)input, ref first, ref second);
                return first == 8 && second == 8 ? 0 : 1;
            }

            private static void Forward(byte value, ref uint first, ref uint second)
            {
                Apply(value, ref first, ref second);
            }

            private static void Apply(byte value, ref uint first, ref uint second)
            {
                first += value;
                second += first;
            }
        }
        """;

    private const string MemoryMarshalSource = """
        using System;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new byte[] { (byte)input };
                ref byte value = ref values[0];
                return Read(ref value, 1) == input ? 0 : 1;
            }

            private static int Read(ref byte value, int length)
            {
                var values = MemoryMarshal.CreateReadOnlySpan(ref value, length);
                return values[0];
            }
        }
        """;

    private const string ForwardedSpanAccumulatorSource = """
        using System;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new byte[] { (byte)input };
                ref byte value = ref values[0];
                uint first = 1;
                uint second = 0;
                Forward(ref value, 1, ref first, ref second);
                return first == 8 && second == 8 ? 0 : 1;
            }

            private static void Forward(ref byte value, int length, ref uint first, ref uint second)
            {
                foreach (var item in MemoryMarshal.CreateReadOnlySpan(ref value, length))
                {
                    first += item;
                    second += first;
                }

                first %= 65521;
                second %= 65521;
            }
        }
        """;

    private const string ScalarStateRoundTripSource = """
        using System;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new byte[] { (byte)input };
                var state = Update(values, 1);
                return state == 0x00080008 ? 0 : 1;
            }

            private static uint Update(ReadOnlySpan<byte> source, uint state)
            {
                uint first = state & 0xFFFF;
                uint second = state >> 16;
                ref byte value = ref MemoryMarshal.GetReference(source);
                Apply(ref value, source.Length, ref first, ref second);
                return (second << 16) | first;
            }

            private static void Apply(ref byte value, int length, ref uint first, ref uint second)
            {
                foreach (var item in MemoryMarshal.CreateReadOnlySpan(ref value, length))
                {
                    first += item;
                    second += first;
                }

                first %= 65521;
                second %= 65521;
            }
        }
        """;

    private const string SlicedScalarLoopSource = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new byte[] { (byte)input };
                var state = Update(values, 1);
                return state == 0x00080008 ? 0 : 1;
            }

            private static uint Update(ReadOnlySpan<byte> source, uint state)
            {
                uint first = state & 0xFFFF;
                uint second = state >> 16;

                while (!source.IsEmpty)
                {
                    var count = source.Length > 5552 ? 5552 : source.Length;
                    foreach (var item in source.Slice(0, count))
                    {
                        first += item;
                        second += first;
                    }

                    source = source.Slice(count);
                    first %= 65521;
                    second %= 65521;
                }

                return (second << 16) | first;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesNestedScalarReferenceForwarding(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var exitCode = executor.Execute(new CompilationScenario(
            "NestedScalarByReference",
            Source,
            "EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesReadOnlySpanCreatedFromForwardedReference(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var exitCode = executor.Execute(new CompilationScenario(
            "ReadOnlySpanFromForwardedReference",
            MemoryMarshalSource,
            "EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesForwardedSpanAccumulators(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var exitCode = executor.Execute(new CompilationScenario(
            "ForwardedSpanAccumulators",
            ForwardedSpanAccumulatorSource,
            "EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesScalarStateRoundTrips(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var exitCode = executor.Execute(new CompilationScenario(
            "ScalarStateRoundTrip",
            ScalarStateRoundTripSource,
            "EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesSlicedScalarLoops(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var exitCode = executor.Execute(new CompilationScenario(
            "SlicedScalarLoop",
            SlicedScalarLoopSource,
            "EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(0, exitCode);
    }
}
