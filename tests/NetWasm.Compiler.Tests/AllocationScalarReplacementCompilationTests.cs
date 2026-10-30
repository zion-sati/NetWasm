using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An object reached only through locals for its fields becomes a local per
// field once its constructor and calls are inlined: foreach's enumerator over
// an array seen as an interface, also anew on each round of an outer loop, a
// class allocated in a loop, copied and tested for null, and fields written
// in a handler. An object
// passed to a call, a finalizable one, a narrow field's compound assignment and
// a type whose static constructor construction must run keep their allocations.
public sealed partial class AllocationScalarReplacementCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private sealed class Point
            {
                public int X;
                public long Y;
                public double Z;
                public string? Name;

                public Point(int x, long y)
                {
                    X = x;
                    Y = y;
                }
            }

            private sealed class Small
            {
                public byte B;
                public Small(byte b) { B = b; }
            }

            private sealed class Finalized
            {
                public int Value;
                public Finalized(int value) { Value = value; }
                ~Finalized() { }
            }

            // Without beforefieldinit, constructing an instance runs the static constructor.
            private sealed class Counted
            {
                static Counted() { Initialized++; }
                public int Value;
                public Counted(int value) { Value = value; }
            }

            private static int Initialized;
            private static int[]? Values;

            private static IEnumerable<int> Sequence() => Values!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ViaInterface(int input)
            {
                IEnumerable<int> values = Values!;
                var sum = input;
                foreach (var value in values) sum = sum * 31 + value;
                return sum;
            }

            // A new enumerator on every round of the outer loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ViaInterfaceInLoop()
            {
                var sum = 0;
                for (var round = 0; round < 3; round++)
                {
                    IEnumerable<int> values = Sequence();
                    foreach (var value in values) sum ^= (value + round) << 3;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Points(int input)
            {
                long total = 0;
                for (var i = 0; i < 20; i++)
                {
                    var point = new Point(input + i, i * 1000L);
                    point.Z = point.X * 0.5;
                    var suffix = i % 3 == 0 ? "ab" : "";
                    if (point.Name == null) point.Name = "n" + suffix;
                    Point? other = point;
                    if (other != null) total += point.X + point.Y + (long)point.Z + point.Name.Length;
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Guarded(int input)
            {
                var point = new Point(input, 1);
                try
                {
                    point.X = checked(point.X * 1_000_000);
                }
                catch (OverflowException)
                {
                    point.X = -1;
                }
                return point.X;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(Point point) => point.X;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Escapes(int input)
            {
                var point = new Point(input, 3);
                point.X += 2;
                return Read(point) + (int)point.Y;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Narrow(int input)
            {
                var small = new Small(unchecked((byte)input));
                small.B += 200;
                return small.B;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Final(int input)
            {
                var finalized = new Finalized(input);
                finalized.Value *= 3;
                return finalized.Value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Precise(int input)
            {
                var before = Initialized;
                var counted = new Counted(input);
                return before * 1000 + Initialized * 100 + counted.Value;
            }

            public static int Run(int input)
            {
                Values = [input, input * 2, input * 3, 7];
                return ViaInterface(input) + ViaInterfaceInLoop() * 19 + (int)(Points(input) % 1_000_003) * 3 +
                    Guarded(input) * 5 + Escapes(input) * 7 + Narrow(input) * 11 + Final(input) * 13 + Precise(input) * 17;
            }
        }
        """;

    // The methods whose allocations the pass replaces; without it, each allocates.
    private static readonly string[] Replaceable = ["ViaInterface", "ViaInterfaceInLoop", "Points", "Guarded"];

    // As desktop .NET computes them, each input in a new process.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 575644 },
        { 1, 6530527 },
        { 2, 12487842 },
        { 5, 30352491 },
        { 40, 238795284 },
        { 5000, 483551791 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3ReplacesOnlyNonEscapingAllocations(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"AllocationScalarReplacement_{target}", Source);

        var replaced = Compile(assets, assembly, target, null);
        var kept = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(AllocationScalarReplacement: ManagedAllocationScalarReplacement.Disabled));

        var allocating = Allocating(assets, replaced);
        Assert.Equal(["Escapes", "Final", "Narrow", "Precise"], allocating
            .Intersect([.. Replaceable, "Escapes", "Narrow", "Final", "Precise"]).Order());
        Assert.Empty(Replaceable.Except(Allocating(assets, kept)));
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                replaced.ApplicationModule, assets.Directory, input, target, replaced.StaticDataEnd));
        }
        Assert.Equal(6530527, ExecuteWithStandardWasiNode(
            kept.ApplicationModule, assets.Directory, 1, target, kept.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReplacedAllocationsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("AllocationScalarReplacementOz", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: WasmTarget.Wasm32, Optimization: FinalWasmOptimization.Oz));

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // The EntryPoint methods that call the runtime's object allocation.
    private static string[] Allocating(TestAssets assets, CompilationResult result)
    {
        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var allocate = AllocateImport().Matches(printed.StandardOutput).Select(static match => match.Groups[1].Value).ToArray();
        Assert.NotEmpty(allocate);
        return [.. FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ')], Body: function))
            .Where(function => function.Name.StartsWith("$EntryPoint::", StringComparison.Ordinal) &&
                allocate.Any(name => Regex.IsMatch(function.Body, $@"^\s*call {Regex.Escape(name)}\s*$", RegexOptions.Multiline)))
            .Select(static function => function.Name["$EntryPoint::".Length..])];
    }

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"\(import ""[^""]+"" ""(?:allocate|allocate_finalizable)"" \(func (\$\S+)")]
    private static partial Regex AllocateImport();
}
