using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A span argument that inlined Slice and indexer calls take the address of,
// as Linq's Sum slices its span, splits into a byref local and a length, as a
// span local does, and a vector's indexer on a local or an argument reads the
// vector's value, so SumSpan, Replaced, Lane and Lanes need no value frame;
// an instance method's vector argument reads its lane the same way, and a lane
// through a reference to a vector reads it in place. A
// span whose address a callee takes keeps its storage and sees the callee's
// change, and ranges and from-end indexes slice as .NET's do.
public sealed partial class SpanArgumentSplitCompilationTests
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            // Linq Sum's shape: a span argument sliced in a loop through inlined
            // accessors, and a vector local whose lanes the indexer reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumSpan(ReadOnlySpan<int> span)
            {
                var accumulator = Vector128<int>.Zero;
                while (span.Length >= Vector128<int>.Count)
                {
                    accumulator += Vector128.Create(span);
                    span = span.Slice(Vector128<int>.Count);
                }
                var sum = 0;
                for (var i = 0; i < Vector128<int>.Count; i++) sum = sum * 3 + accumulator[i];
                foreach (var value in span) sum += value;
                return sum;
            }

            // A span argument replaced before it is read.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Replaced(ReadOnlySpan<int> span, int skip)
            {
                if (skip > 0) span = span.Slice(skip);
                var hash = span.Length;
                for (var i = 0; i < span.Length; i++) hash = hash * 31 + span[i];
                return hash;
            }

            // Ranges and from-end indexes on a span argument.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Ranged(ReadOnlySpan<int> span, int skip) =>
                span[skip..][^1] * 1000 + span[..^skip].Length * 10 + span[skip..^1].Length;

            // A lane of a vector argument, at an index the caller chooses.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lane(Vector128<int> vector, int index) => vector[index];

            // Lanes read in place through a reference and an in parameter.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RefLane(ref Vector128<int> vector, in Vector128<int> other, int index) =>
                vector[index] * 10 + other[3 - index];

            private sealed class Reader
            {
                // A lane of an instance method's vector argument.
                [MethodImpl(MethodImplOptions.NoInlining)]
                public int Lane(Vector128<int> vector, int index) => vector[index];
            }

            // Indexes the rewrite leaves alone: one computed by several instructions,
            // and one a branch chooses, which reaches the indexer on two paths.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Guarded(Vector128<int> vector, int index, bool flag) =>
                vector[index + 1] * 10 + vector[flag ? 0 : 3];

            // Every lane of a Vector<T> local holds the same value, so the result does
            // not depend on how many lanes the vector has.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Lanes(Vector<long> vector)
            {
                var accumulator = vector * 3;
                long sum = 0;
                for (var i = 0; i < Vector<long>.Count; i++) sum += accumulator[i];
                return sum / Vector<long>.Count;
            }

            // A span whose address a callee takes, which changes it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Escaping(ReadOnlySpan<int> span) => Peek(ref span) * 100 + span.Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Peek(ref ReadOnlySpan<int> span)
            {
                var first = span[0];
                span = span[1..];
                return first;
            }

            public static int Run(int input)
            {
                var values = new int[input + 9];
                for (var i = 0; i < values.Length; i++) values[i] = i * i - input;
                var result = SumSpan(values) + Replaced(values, input % 3) * 3 + Escaping(values) * 5 + Ranged(values, input % 4 + 1) * 19;
                try
                {
                    result += Ranged(values, values.Length + 1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    result += 23;
                }
                result += Lane(Vector128.Create(input, 2 * input, 3 * input, 4 * input), input & 3) * 7;
                result += (int)Lanes(new Vector<long>(input + 1)) * 11;
                result += Guarded(Vector128.Create(input, input + 1, input + 2, input + 3), input & 1, (input & 2) != 0) * 17;
                var lanes = Vector128.Create(input, input * 5, 9, 11);
                result += RefLane(ref lanes, in lanes, input & 3) * 29 + new Reader().Lane(lanes, (input + 1) & 3) * 31;
                try
                {
                    result += Lane(Vector128<int>.One, input + 4);
                }
                catch (ArgumentOutOfRangeException)
                {
                    result += 13;
                }
                return result;
            }
        }
        """;

    // As .NET 11 computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, -1491413189 },
        { 1, -1320619010 },
        { 2, -330096853 },
        { 5, -362602891 },
        { 13, -532406722 },
        { 40, 1342277702 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SpanArgumentsAndVectorLanesNeedNoValueFrame(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"SpanArgumentSplit_{target}", Source);

        var split = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var whole = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(StructScalarReplacement: ManagedStructScalarReplacement.Disabled));

        string[] probes = ["SumSpan", "Replaced", "Lane", "Lanes"];
        Assert.Equal(probes.ToDictionary(static probe => probe, static _ => false), Framed(assets, split, probes));
        Assert.True(Framed(assets, whole, ["SumSpan"])["SumSpan"]);
        foreach (var (input, expected) in Cases.Select(row => row.Data))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                split.ApplicationModule, assets.Directory, input, target, split.StaticDataEnd));
        }
        Assert.Equal(-1320619010, ExecuteWithStandardWasiNode(
            whole.ApplicationModule, assets.Directory, 1, target, whole.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SplitSpanArgumentsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SpanArgumentSplitOz", Source);
        var result = Compile(assets, assembly, WasmTarget.Wasm32, FinalWasmOptimization.Oz, null);

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Whether each EntryPoint method enters a value frame.
    private static Dictionary<string, bool> Framed(TestAssets assets, CompilationResult result, string[] names)
    {
        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ', StringComparison.Ordinal)], Body: function))
            .Where(function => names.Any(name => function.Name == "$EntryPoint::" + name))
            .ToDictionary(static function => function.Name["$EntryPoint::".Length..], static function => FrameEnter().IsMatch(function.Body));
    }

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        FinalWasmOptimization optimization,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: optimization, OptimizationOverrides: overrides));

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$\S*value_frame_enter")]
    private static partial Regex FrameEnter();
}
