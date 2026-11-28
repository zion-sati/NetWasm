// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// The optimizing presets version span loops: a test ahead of the loop picks a copy whose element accesses check nothing,
// where no element the loop reads is at address 0, and the loop as written elsewhere (NullManagedAddressCompilationTests
// holds every level to O0's throws). None keeps each access's check. A null check is an address's eqz, an if and the
// NullReferenceException throw; a throw's own test of its exception, which then throws the exception, is not one.
public sealed class ByRefLoopVersioningCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public struct Pair
        {
            public int A;
            public int B;
        }

        public static class Loops
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Hash(ReadOnlySpan<char> text)
            {
                var hash = 17;
                for (var index = 0; index < text.Length; index++) hash = hash * 31 + text[index];
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Prefix(Span<int> values)
            {
                var sum = 1;
                for (var index = 0; index < values.Length; index++)
                {
                    sum += values[index];
                    values[index] = sum - values[index];
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Find(ReadOnlySpan<int> values, int target)
            {
                for (var index = 0; index < values.Length; index++) if (values[index] == target) return index;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Fields(ReadOnlySpan<Pair> pairs)
            {
                var total = 1;
                foreach (ref readonly var pair in pairs) total += pair.A * pair.B;
                return total;
            }

            // An int index Unsafe.Add sign-extends, two elements ahead of the counter (one ahead could never pass int).
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int AheadSigned(ref int first, int count)
            {
                var total = 1;
                for (var index = 0; index < count; index++) total ^= Unsafe.Add(ref first, index + 2);
                return total;
            }

            // One element ahead, which an int bound cannot carry past int.MaxValue.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int AheadOne(ref int first, int count)
            {
                var total = 1;
                for (var index = 0; index < count; index++) total ^= Unsafe.Add(ref first, index + 1);
                return total;
            }

            // Reads at an offset from the counter by values the loop leaves alone, which bounds versioning proves in
            // range at O3: a three-tap stencil, the row above at start + index - width, and a lookup at an index read
            // from memory, which only its own check bounds.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Stencil(ReadOnlySpan<int> source, int start, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    var at = start + index;
                    total += source[at - 1] + source[at] + source[at + 1];
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void AddAbove(Span<int> pixels, int start, int count, int width)
            {
                for (var index = 0; index < count; index++)
                {
                    var at = start + index;
                    pixels[at] += pixels[at - width] | 1;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Lookup(ReadOnlySpan<int> keys, ReadOnlySpan<int> table, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++) total ^= table[keys[index] & 15] + index;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Cursor(ReadOnlySpan<int> values)
            {
                ref var start = ref MemoryMarshal.GetReference(values);
                var total = 1;
                for (nint index = 0; index < values.Length; index++) total ^= Unsafe.Add(ref start, index) * 3;
                return total;
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new int[64];
                var pairs = new Pair[64];
                var text = new char[64];
                for (var index = 0; index < 64; index++)
                {
                    values[index] = index * 7 % 101 + 1;
                    pairs[index] = new Pair { A = index, B = index % 13 + 1 };
                    text[index] = (char)('a' + index % 26);
                }
                values[40] = input;
                var hash = Loops.Hash(text);
                var expected = 17;
                foreach (var character in text) expected = expected * 31 + character;
                if (hash != expected) return 1;
                if (Loops.Find(values, input) != 40) return 2;
                var fields = 1;
                foreach (var pair in pairs) fields += pair.A * pair.B;
                if (Loops.Fields(pairs) != fields) return 3;
                var cursor = 1;
                foreach (var value in values) cursor ^= value * 3;
                if (Loops.Cursor(values) != cursor) return 4;
                var ahead = 1;
                for (var index = 2; index < 64; index++) ahead ^= values[index];
                // Counts from the input, so the calls are not specialized on a constant bound.
                if (Loops.AheadSigned(ref values[0], input + 20) != ahead) return 6;
                var one = 1;
                for (var index = 1; index < 64; index++) one ^= values[index];
                if (Loops.AheadOne(ref values[0], input + 21) != one) return 7;
                var copy = (int[])values.Clone();
                var sum = 1;
                for (var index = 0; index < 64; index++)
                {
                    sum += copy[index];
                    copy[index] = sum - copy[index];
                }
                if (Loops.Prefix(values) != sum || !values.AsSpan().SequenceEqual(copy)) return 5;
                // The offset loops, with starts and counts from the input so no call is specialized on a constant.
                var stencil = 0;
                for (var at = 1; at < input - 1; at++) stencil += values[at - 1] + values[at] + values[at + 1];
                if (Loops.Stencil(values, 1, input - 2) != stencil) return 8;
                var image = (int[])values.Clone();
                for (var at = input - 30; at < input - 10; at++) image[at] += image[at - 8] | 1;
                Loops.AddAbove(values, input - 30, 20, 8);
                if (!values.AsSpan().SequenceEqual(image)) return 9;
                var table = new int[16];
                for (var index = 0; index < 16; index++) table[index] = text[index] * 3;
                var lookup = 0;
                for (var index = 0; index < input; index++) lookup ^= table[values[index] & 15] + index;
                if (Loops.Lookup(values, table, input) != lookup) return 10;
                return input;
            }
        }
        """;

    // Versioned at O3 only, where bounds versioning runs, by its test of each span's run.
    private static readonly string[] BoundsVersioned = ["$Loops::Stencil", "$Loops::AddAbove", "$Loops::Lookup"];

    private static readonly string[] Versioned =
        ["$Loops::Hash", "$Loops::Prefix", "$Loops::Find", "$Loops::Fields", "$Loops::Cursor", "$Loops::AheadSigned", "$Loops::AheadOne"];

    [Theory]
    [InlineData(FinalWasmOptimization.O1, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm32)]
    public void TheOptimizingPresetsCopyEachLoopWithoutItsChecks(FinalWasmOptimization optimization, WasmTarget target)
    {
        var functions = Compile(optimization, target, out var result, out var assets);
        using (assets)
        {
            foreach (var name in Versioned)
            {
                var loops = Loops(functions[name]);
                Assert.True(loops.Count >= 2, $"{name} has {loops.Count} loops");
                Assert.True(loops.Any(static loop => !NullCheck().IsMatch(loop)), $"every loop of {name} checks an address");
            }
            Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd));
        }
    }

    // At O3 bounds versioning's copy proves each offset read in range, and its test of the span's whole run proves none
    // of them at address 0, so one loop of each function checks no address.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OffsetReadsInABoundsVersionedCopyCheckNoAddress(WasmTarget target)
    {
        var functions = Compile(FinalWasmOptimization.O3, target, out var result, out var assets);
        using (assets)
        {
            foreach (var name in BoundsVersioned)
            {
                var loops = Loops(functions[name]);
                Assert.True(loops.Count >= 2, $"{name} has {loops.Count} loops");
                Assert.True(loops.Any(static loop => !NullCheck().IsMatch(loop)), $"every loop of {name} checks an address");
            }
            Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd));
        }
    }

    // A read whose int index Unsafe.Add sign-extends, k elements ahead of the counter, versions only while the last index,
    // bound - 1 + k, stays within int: the test compares the bound with int.MaxValue - k + 1 (a cgt after the constant),
    // which an int bound always passes for k = 1, so a read one element ahead has none.
    [Fact]
    public void ASignExtendedIndexAheadOfTheCounterIsBoundedByIntMaxValue()
    {
        var functions = Compile(FinalWasmOptimization.O3, WasmTarget.Wasm32, out var result, out var assets);
        using (assets)
        {
            Assert.True(BoundTests(result, "AheadSigned", int.MaxValue - 1) == 1, Listing(result, "AheadSigned"));
            Assert.True(BoundTests(result, "AheadOne", int.MaxValue) == 0, Listing(result, "AheadOne"));
            Assert.True(Loops(functions["$Loops::AheadOne"]).Count >= 2, "AheadOne is not versioned");
            Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, WasmTarget.Wasm32,
                result.StaticDataEnd));
        }

        static ImmutableArray<CilInstruction> Instructions(CompilationResult result, string name) =>
            result.Program.Methods.Values.Single(method => method.Method.Definition.Name == name).Body.Instructions;

        static string Listing(CompilationResult result, string name) =>
            string.Join('\n', Instructions(result, name).Select(static instruction => $"{instruction.Operation} {instruction.Operand}"));

        static int BoundTests(CompilationResult result, string name, int constant)
        {
            var instructions = Instructions(result, name);
            return Enumerable.Range(0, instructions.Length - 1).Count(index =>
                instructions[index] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var value } } &&
                value == constant && instructions[index + 1].Operation == CilOperation.CompareGreaterThanSigned);
        }
    }

    [Fact]
    public void NoneKeepsEachAccessCheck()
    {
        var functions = Compile(FinalWasmOptimization.None, WasmTarget.Wasm32, out var result, out var assets);
        using (assets)
        {
            foreach (var name in Versioned)
            {
                var loops = Loops(functions[name]);
                Assert.NotEmpty(loops);
                Assert.All(loops, static loop => Assert.Matches(NullCheck(), loop));
            }
            Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, WasmTarget.Wasm32,
                result.StaticDataEnd));
        }
    }

    private static Dictionary<string, string> Compile(
        FinalWasmOptimization optimization, WasmTarget target, out CompilationResult result, out TestAssets assets)
    {
        assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"ByRefLoopVersioning_{optimization}_{target}", Source);
        result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return Regex.Split(printed.StandardOutput, @"\n\s*\(func ")
            .Skip(1)
            .ToDictionary(static function => function[..function.IndexOfAny([' ', '\n'])], StringComparer.Ordinal);
    }

    private static Regex NullCheck() => new(
        @"i(32|64)\.eqz\s+if ;; label = @\d+\s+i32\.const 1\s+call \$managed\.throw\s+unreachable\s+end(?!\s+local\.get \d+\s+call \$managed\.throw)");

    // The text of each outermost loop of a printed function: the printer indents a construct's body past its opening
    // line, so a loop runs until a line indented no further than its own.
    private static List<string> Loops(string function)
    {
        var lines = function.Split('\n');
        var loops = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith("loop", StringComparison.Ordinal)) continue;
            var indent = Indent(lines[index]);
            var end = index + 1;
            while (end < lines.Length && (lines[end].Trim().Length == 0 || Indent(lines[end]) > indent)) end++;
            loops.Add(string.Join('\n', lines[index..end]));
            index = end - 1;
        }
        return loops;

        static int Indent(string line) => line.Length - line.TrimStart().Length;
    }
}
