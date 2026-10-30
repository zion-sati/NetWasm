using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Pair<int>.M(T) and Pair<int>.M(int) have the same signature once T is int, as
// Vector<byte>(ReadOnlySpan<T>) and Vector<byte>(ReadOnlySpan<byte>) do. A call
// names its overload by the declared signature, where T is still T, so each
// reference resolves to the overload C# chose, and Box<T>'s N(T) implements
// IShape<T>.N for Box<int> as well.
public sealed class CoincidingOverloadCompilationTests
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;

        public sealed class Pair<T>
        {
            public int M(T value) => 1;
            public int M(int value) => 2;
            public int Call(T value) => M(value);
        }

        public interface IShape<T>
        {
            int N(T value);
        }

        public sealed class Box<T> : IShape<T>
        {
            public int N(T value) => 3;
            public int N(int value) => 4;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Through<U>(IShape<U> shape, U value) => shape.N(value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Generic<U>(Pair<U> pair, U value) => pair.M(value);

            public static int Run(int input)
            {
                var pair = new Pair<int>();
                var bytes = new byte[64];
                for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index * 3 + input);
                var vector = new Vector<byte>(new ReadOnlySpan<byte>(bytes));
                var span = new Vector<byte>(bytes.AsSpan());
                return pair.M(input) * 1000 + pair.Call(input) * 100 + Generic(pair, input) * 10 +
                    Generic(new Pair<string>(), "x") + vector[5] * 10000 + span[7] * 1000000 +
                    ((IShape<int>)new Box<int>()).N(input) * 100000000 + Through(new Box<int>(), input) * 1000000000;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    public void CallsResolveToTheDeclaredOverload(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"CoincidingOverload_{target}_{preset}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: preset));

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, -973815185), (1, -972805185), (7, -966745185) })
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
