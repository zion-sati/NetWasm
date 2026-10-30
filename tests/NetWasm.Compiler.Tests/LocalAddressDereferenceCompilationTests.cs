using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class LocalAddressDereferenceCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public enum Level { Low, Mid = 3, High = 7 }

        public static class EntryPoint
        {
            private static bool TryHalve(int value, out int half)
            {
                half = value / 2;
                return (value & 1) == 0;
            }

            private static void Bump(ref long value) => value += 3;

            private static void Clear<T>(out T value) => value = default!;

            public static int Run(int input)
            {
                var even = TryHalve(input, out var half) ? 1 : 0;
                var wide = input * 0x1_0000_0001L;
                Bump(ref wide);
                Clear(out double scale);
                scale += input;
                var bits = (uint)input;
                return half * 1000 + even * 100 + wide.GetHashCode() + (int)(scale * 10) + bits.GetHashCode();
            }

            private static int Read(in byte value) => value;

            private static int Value(in Level value) => (int)value;

            private static void Set(ref byte value, int to) => value = (byte)to;

            public static int Narrow(int input)
            {
                var b = (byte)input;
                var s = (sbyte)input;
                var c = (char)input;
                var h = (short)input;
                var odd = (input & 1) != 0;
                var level = (Level)(input & 7);
                return Read(in b) + s.GetHashCode() * 3 + (c.Equals('\u012c') ? 5 : 0) + Math.Sign(h.CompareTo((short)3)) * 7 +
                    odd.GetHashCode() * 11 + Value(in level) * 13 + Math.Sign(s.CompareTo((sbyte)-1)) * 17;
            }

            public static int NarrowStore(int input)
            {
                var b = (byte)input;
                Set(ref b, input + 1);
                return b * 3 + b.GetHashCode();
            }

            public static int Reinterpret(int input)
            {
                var x = input;
                Unsafe.As<int, byte>(ref x) = 0x7f;
                var f = input + 0.5f;
                return x ^ Unsafe.As<float, int>(ref f);
            }
        }
        """;

    // Inlined out and ref parameters and primitive receivers leave the
    // caller's local accessed through its address, which put it in the value
    // frame; at O3 those accesses read and write the local itself.
    [Fact]
    public void O3AccessesInlinedOutRefAndReceiverLocalsDirectly()
    {
        var methods = CompileO3();

        Assert.DoesNotContain(methods["Run"].Instructions, instruction =>
            instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index local } &&
            methods["Run"].LocalSignatureTypes[local.Value].Shape == CliTypeShape.Primitive);
    }

    // Narrow primitives and enums are read through the address the same way,
    // and an enum is accessed as its underlying type.
    [Fact]
    public void O3ReadsInlinedNarrowAndEnumLocalsDirectly()
    {
        var methods = CompileO3();

        Assert.DoesNotContain(methods["Narrow"].Instructions, instruction =>
            instruction.Operation == CilOperation.LoadLocalAddress);
    }

    // A store through the address truncates a narrow value and a local store
    // may not, so the stored local keeps its address; its reads still fold.
    [Fact]
    public void O3KeepsTheAddressOfANarrowLocalStoredThroughIt()
    {
        var methods = CompileO3();

        Assert.Single(methods["NarrowStore"].Instructions, instruction =>
            instruction.Operation == CilOperation.LoadLocalAddress);
    }

    // A narrower store, or a read as a different primitive, depends on the
    // local's memory, so its address stays.
    [Fact]
    public void O3KeepsTheAddressOfALocalReinterpretedThroughIt()
    {
        var methods = CompileO3();

        Assert.Equal(2, methods["Reinterpret"].Instructions.Count(instruction =>
            instruction.Operation == CilOperation.LoadLocalAddress));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 0, 103)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 1, 16)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 6, 3181)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", -9, -4087)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Run", 123456789, -1338090748)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, "Run", 7, 3090)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, "Run", int.MinValue, 96)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Narrow", 0, 10)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Narrow", 1, 38)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Narrow", 300, 257)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Narrow", -5, 262)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, "Narrow", 7, 154)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, "Narrow", 2, 44)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "NarrowStore", 0, 4)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "NarrowStore", 300, 180)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "NarrowStore", -5, 1008)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, "NarrowStore", 7, 32)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, "NarrowStore", 1, 8)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Reinterpret", 0, 1056964735)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3, "Reinterpret", 123456789, 1269871836)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3, "Reinterpret", -9, 1056440191)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz, "Reinterpret", int.MinValue, 1325400191)]
    public void AccessesThroughLocalAddressesMatchDesktop(
        WasmTarget target,
        FinalWasmOptimization optimization,
        string method,
        int input,
        int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"LocalAddressDereference_{method}_{target}_{optimization}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])
        {
            EntryMethodName = method,
            Optimization = optimization,
        }));
    }

    private static Dictionary<string, CilMethodBody> CompileO3()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("LocalAddressDereference", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [
                new RequestedExport("reinterpret", "EntryPoint", "Reinterpret"),
                new RequestedExport("narrow", "EntryPoint", "Narrow"),
                new RequestedExport("narrowStore", "EntryPoint", "NarrowStore"),
            ],
            Optimization: FinalWasmOptimization.O3));
        return compilation.Program.Methods.Values
            .Where(method => method.Method.DeclaringType.FullName == "EntryPoint" &&
                method.Method.Definition.Name is "Run" or "Reinterpret" or "Narrow" or "NarrowStore")
            .ToDictionary(method => method.Method.Definition.Name, method => method.Body);
    }
}
