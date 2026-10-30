using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class SpeedInliningCompilationTests
{
    private const string Source = """
        using System;
        public sealed class Counter
        {
            private int _count;
            public int Count => _count;
            public void Add(int value) => _count += value;
            public int Twice() => _count * 2;
            public int Answer() => 42;
        }
        public static class EntryPoint
        {
            private static int Next(int value) => value + 1;
            private static int Square(int value) => Next(value) * Next(value);
            public static int Run(int input)
            {
                var counter = new Counter();
                var total = 0;
                try
                {
                    for (var index = 0; index < input; index++)
                    {
                        counter.Add(Next(index));
                        total += Square(index);
                    }
                }
                finally
                {
                    total += counter.Count;
                }
                var missing = input > 100 ? counter : null;
                // The receiver's null check still precedes each inlined body.
                try { total += missing!.Twice(); } catch (NullReferenceException) { total += 1000; }
                try { total += missing!.Answer(); } catch (NullReferenceException) { total += 2000; }
                return total;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void InlinesSmallCalleesAtO3AndInsideLoopsAtOzAndKeepsTheirSemantics(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("SpeedInlining", Source) : assets.CompileSource("SpeedInlining", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        var methods = result.Program.Methods.Values
            .Select(body => $"{body.Method.DeclaringType.FullName}::{body.Method.Definition.Name}")
            .ToHashSet();

        // Next is reached only through Square, so a nested round removes it too. Oz inlines the calls inside
        // the loop and field accessors anywhere, so Count (in the finally) goes; Twice (after the loop) stays a call.
        string[] inlined = ["EntryPoint::Next", "EntryPoint::Square", "Counter::Add", "Counter::get_Count", "Counter::Twice"];
        string[] remaining = preset switch
        {
            FinalWasmOptimization.O3 => [],
            FinalWasmOptimization.Oz => ["Counter::Twice"],
            _ => inlined,
        };
        Assert.Equal(remaining, inlined.Where(methods.Contains));
        // Answer never touches its receiver, so a virtual call to it keeps its null check.
        Assert.Contains("Counter::Answer", methods);
        // 1^2 + ... + 5^2 = 55, Count = 1 + ... + 5 = 15.
        Assert.Equal(3070, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 5, target, result.StaticDataEnd));
        Assert.Equal(3000, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    private const string ValueSource = """
        using System;
        public struct Pair
        {
            public int Left;
            public int Right;
            public Pair(int left, int right) { Left = left; Right = right; }
            public int Sum => Left + Right;
            public void Swap() => (Left, Right) = (Right, Left);
            public Pair Doubled() => new(Left * 2, Right * 2);
        }
        public static class Counts<T>
        {
            public static int Next(T[] values) => values.Length + 1;
        }
        public static class EntryPoint
        {
            private static bool StartsWith(ReadOnlySpan<int> span, int value) => !span.IsEmpty && span[0] == value;
            private static int At(ReadOnlySpan<int> span, int index) => span[index];
            private static T Last<T>(T[] values, T fallback) => values.Length == 0 ? fallback : values[values.Length - 1];
            public static int Run(int input)
            {
                var pair = new Pair(input, 3);
                pair.Swap();
                var doubled = pair.Doubled();
                var values = new int[input];
                for (var index = 0; index < values.Length; index++) values[index] = index * 7;
                var total = pair.Sum * 100 + doubled.Left * 10 + doubled.Right + Counts<int>.Next(values) + Last(values, -1);
                total += StartsWith(values, 0) ? 1000 : 2000;
                total += (int)((input + 7.25m) / (input + 1.5m) * 100m);
                // The span indexer throws from its inlined body.
                try { total += At(values, input); } catch (IndexOutOfRangeException) { total += 5000; }
                return total;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void InlinesValueTypeMembersConstructorsAndGenericInstantiations(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("ValueInlining", ValueSource) : assets.CompileSource("ValueInlining", ValueSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        // Instantiations are kept apart, and their names carry the assembly and type arguments.
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values).Select(Name).ToHashSet();

        string[] inlined = ["Pair::get_Sum", "Pair::Swap", "Pair::.ctor", "Counts`1::Next", "EntryPoint::Last"];
        Assert.Equal(preset == FinalWasmOptimization.O3 ? [] : inlined, inlined.Where(methods.Contains));
        foreach (var input in new[] { 0, 4 })
        {
            Assert.Equal(Expected(input), ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static string Name(ManagedMethodBody body)
    {
        var type = body.Method.DeclaringType.FullName ?? body.Method.DeclaringType.CanonicalName;
        return $"{type[(type.IndexOf(']') + 1)..].Split('<')[0]}::{body.Method.Definition.Name}";
    }

    private static int Expected(int input)
    {
        var (left, right) = (3, input);
        var total = (left + right) * 100 + left * 2 * 10 + right * 2 + input + 1 + (input == 0 ? -1 : (input - 1) * 7);
        total += input > 0 ? 1000 : 2000;
        total += (int)((input + 7.25m) / (input + 1.5m) * 100m);
        return total + 5000;
    }
}
