using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A static readonly field whose owner's static constructor stores a constant
// in it first reads as that constant: Read loads every Limits and Generic<T>
// field without an initialization check, each stored width reads back as .NET
// reads it, and a field the constructor never sets reads zero. Mixed's
// constants fold and still run its constructor at their first read. A computed
// initializer, one that calls a method, a field read before its constant is
// stored, and one the constructor's body stores again keep their loads.
public sealed partial class ConstantStaticFieldCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public enum Mode : short { Off = 0, On = 7, Max = 300 }

        // Constants only: every load folds.
        public static class Limits
        {
            public static readonly bool Enabled = true;
            public static readonly byte Small = unchecked((byte)300);
            public static readonly sbyte Negative = -5;
            public static readonly char Letter = 'Q';
            public static readonly short Short = -1234;
            public static readonly ushort Unsigned = 65000;
            public static readonly int Count = 123456;
            public static readonly uint Mask = 0xF0F0F0F0;
            public static readonly long Small64 = 42;
            public static readonly long Large64 = long.MinValue + 3;
            public static readonly ulong Unsigned64 = 4000000000;
            public static readonly float Half = 0.5f;
            public static readonly double Third = 1.0 / 3.0;
            public static readonly Mode Current = Mode.Max;
            public static readonly int Unset;
            public static readonly string Name = null;
        }

        // Constants first, then work: the loads fold and still run the constructor.
        public static class Mixed
        {
            public static readonly int Max = 100;
            public static readonly bool Flag = true;
            public static readonly int[] Table = Build();

            static Mixed() => Marker.Count += 10;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Build()
            {
                Marker.Count++;
                return new int[3];
            }
        }

        public static class Marker
        {
            public static int Count;
        }

        // A call before the constant: Compute reads B while it is still zero.
        public static class Ordered
        {
            public static readonly int A = Compute();
            public static readonly int B = 5;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Compute() => B + 1;
        }

        // The constructor's body stores the field again.
        public static class Restored
        {
            public static readonly int X = 5;
            public static int Seed = 4;

            static Restored() => X = Seed * 2;
        }

        public static class Generic<T>
        {
            public static readonly int Size = 16;
        }

        // A computed value and a call keep their loads.
        public static class Computed
        {
            public static int Seed = 3;
            public static readonly int Value = Seed + 4;
        }

        public static class Calls
        {
            public static int Initialized;
            public static readonly int Value = Initialize();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Initialize()
            {
                Initialized++;
                return 11;
            }
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Read(int input)
            {
                long sum = 0;
                for (var i = 0; i < input + 3; i++)
                {
                    if (Limits.Enabled) sum += Limits.Small + Limits.Negative + Limits.Letter + Limits.Short + Limits.Unsigned;
                    sum = sum * 3 + Limits.Count + Limits.Mask + Limits.Small64 + (Limits.Large64 >> 60) + (long)(Limits.Unsigned64 >> 3);
                    sum += (long)(Limits.Half * 4) + (long)(Limits.Third * 300) + (short)Limits.Current + Limits.Unset;
                    sum += Limits.Name is null ? 1 : 2;
                    sum += Generic<int>.Size + Generic<string>.Size;
                }
                return sum;
            }

            public static int Run(int input)
            {
                var value = Read(input);
                value += Computed.Value * 1000;
                value += Calls.Value * 10000 + Calls.Initialized * 100000;
                value += BitConverter.IsLittleEndian ? 1000000 : 0;
                var before = Marker.Count;
                value += (Mixed.Flag ? Mixed.Max : 0) * 3 + (Marker.Count - before) * 7 + Mixed.Table.Length;
                value += Ordered.A * 11 + Ordered.B * 13 + Restored.X * 17;
                return unchecked((int)(value ^ (value >> 32)));
            }
        }
        """;

    // As .NET 11 computes them, each in a new process.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, -1074033898 },
        { 1, 1318100922 },
        { 2, -95429488 },
        { 5, 611466213 },
        { 13, 1513536838 },
        { 40, 443071282 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    public void ConstantStaticFieldsLoadWithoutInitialization(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"ConstantStaticField_{target}_{optimization}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.False(Initializes(assets, result, "Read"));
        foreach (var (input, expected) in Cases.Select(row => row.Data))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // Whether the EntryPoint method runs a static initialization check.
    private static bool Initializes(TestAssets assets, CompilationResult result, string name)
    {
        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return Initialization().IsMatch(FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Single(function => function.StartsWith("$EntryPoint::" + name + " ", StringComparison.Ordinal)));
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$\S*static\.initialize")]
    private static partial Regex Initialization();
}
