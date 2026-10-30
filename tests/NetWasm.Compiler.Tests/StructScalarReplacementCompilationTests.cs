using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Struct locals used field by field once their operators and constructors are
// inlined become a local per field: doubles in a loop, narrow fields whose
// stores must still truncate, a generic struct, compound assignments, an
// enumerator assigned before its loop, and an address that escapes to a call,
// which keeps its struct whole. So does a field's address that reaches past
// the field, to the struct's other fields.
public sealed class StructScalarReplacementCompilationTests
{
    private const string FieldAddressSource = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public static unsafe class EntryPoint
        {
            private struct Triple
            {
                public int A, B, C;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct Packed
            {
                public byte Tag;
                public long Value;
                public short Tail;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Next(ref int field) => Unsafe.Add(ref field, 1);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref int field) => field++;

            // Each check has its own local, so that no other use keeps it whole.
            private static bool Offset(int input)
            {
                var triple = new Triple { A = input, B = input * 2, C = input * 3 };
                return Unsafe.ByteOffset(ref triple.A, ref triple.C) == 8;
            }

            private static bool Walk(int input)
            {
                var triple = new Triple { A = input, B = input * 2, C = input * 3 };
                Unsafe.Add(ref triple.A, 2) = 7;
                return Unsafe.Add(ref triple.A, 1) == input * 2 && triple.C == 7;
            }

            private static bool Callee(int input)
            {
                var triple = new Triple { A = input, B = input * 2, C = 7 };
                return Next(ref triple.B) == 7;
            }

            private static bool Span(int input)
            {
                var triple = new Triple { A = input, B = input * 2, C = 7 };
                var span = MemoryMarshal.CreateSpan(ref triple.A, 3);
                span[1] = 11;
                return triple.B == 11 && span[0] + span[2] == input + 7;
            }

            private static bool PackedOffsets(int input)
            {
                var packed = new Packed { Tag = 1, Value = input, Tail = 2 };
                return Unsafe.ByteOffset(ref packed.Tag, ref Unsafe.As<long, byte>(ref packed.Value)) == 1 &&
                    Unsafe.ByteOffset(ref packed.Tag, ref Unsafe.As<short, byte>(ref packed.Tail)) == 9;
            }

            private static bool Pointer(int input)
            {
                var triple = new Triple { A = input, B = 2, C = 3 };
                int* first = &triple.A;
                first[2] = 9;
                var second = (int*)Unsafe.AsPointer(ref triple.B);
                return triple.C == 9 && second[-1] == input;
            }

            private static bool Reassigned(int input)
            {
                var first = new Triple { A = input, B = 2, C = 3 };
                var second = new Triple { A = 4, B = 5, C = 6 };
                ref var field = ref first.A;
                if (input > 2) field = ref second.A;
                return Unsafe.Add(ref field, 2) == (input > 2 ? 6 : 3);
            }

            // Once inlined, a parameter or a returned field reference is a local's.
            private static bool Parameter(Triple triple, int input) => Unsafe.Add(ref triple.A, 2) == input;

            private static ref int Middle(ref Triple triple) => ref triple.B;

            private static bool Returned(int input)
            {
                var triple = new Triple { A = input, B = 2, C = 3 };
                return Unsafe.Subtract(ref Middle(ref triple), 1) == input;
            }

            // An address used only for its own field's value.
            private static bool OwnField(int input)
            {
                var counter = new Triple { A = input, B = input };
                ref var first = ref counter.A;
                first += 5;
                Bump(ref counter.B);
                return counter.A == input + 5 && counter.B == input + 1;
            }

            public static int Run(int input) =>
                !Offset(input) ? -1 :
                !Walk(input) ? -2 :
                !Callee(input) ? -3 :
                !Span(input) ? -4 :
                !PackedOffsets(input) ? -5 :
                !Pointer(input) ? -6 :
                !Reassigned(input) ? -7 :
                !Parameter(new Triple { A = 1, B = 2, C = input }, input) ? -8 :
                !Returned(input) ? -9 :
                !OwnField(input) ? -10 :
                42;
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void FieldAddressesThatReachOtherFieldsKeepTheStructWhole(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource($"StructFieldAddress{optimization}{target}", FieldAddressSource);
        var result = Compile(assets, assembly, target, optimization, null);

        foreach (var input in new[] { 1, 5 })
        {
            Assert.Equal(42, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private struct Complex
            {
                public double R, I;
                public Complex(double r, double i) { R = r; I = i; }
                public Complex Square() => new(R * R - I * I, 2.0 * R * I);
                public double Magnitude() => R * R + I * I;
                public static Complex operator +(Complex a, Complex b) => new(a.R + b.R, a.I + b.I);
            }

            // Narrow fields: a store truncates as the field does.
            private struct Packed
            {
                public byte Low;
                public bool Flag;
                public short Signed;
                public char Letter;
                public long Wide;
            }

            private struct Pair<T>
            {
                public T First;
                public T Second;
            }

            private struct Named
            {
                public string Name;
                public int Count;
            }

            private static int Julia(double real, double imaginary)
            {
                var seed = new Complex(real, imaginary);
                var total = 0;
                for (var m = -1.0; m < 1.0; m += 0.25)
                {
                    for (var n = -1.0; n < 1.0; n += 0.25)
                    {
                        var accum = new Complex(m, n);
                        int steps;
                        for (steps = 0; steps < 50; steps++)
                        {
                            if (accum.Magnitude() > 4.0) break;
                            accum = accum.Square() + seed;
                        }
                        total += steps;
                    }
                }
                return total;
            }

            private static long Narrow(int input)
            {
                var packed = new Packed();
                long sum = 0;
                for (var i = 0; i < 40; i++)
                {
                    packed.Low = unchecked((byte)(input * 37 + i * 11));
                    packed.Flag = (i & 1) == 0;
                    packed.Signed = unchecked((short)(input * 9001 - i * 3001));
                    packed.Letter = (char)('A' + (i + input) % 26);
                    packed.Wide += packed.Low * (packed.Flag ? 3L : -5L) + packed.Signed + packed.Letter;
                    sum += packed.Wide ^ packed.Low;
                }
                return sum;
            }

            private static int Generic(int input)
            {
                var pair = new Pair<int> { First = input, Second = input * 3 };
                var copy = pair;
                copy.First += 7;
                pair = default;
                pair.Second = copy.First + copy.Second;
                return pair.First * 1000 + pair.Second + Take(copy);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Take(Pair<int> pair) => pair.First - pair.Second;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref Named named) => named.Count += 100;

            private static int Escaping(int input)
            {
                var named = new Named { Name = "x" + input, Count = input };
                Bump(ref named);
                named.Count *= 2;
                return named.Count + named.Name.Length;
            }

            private static int Enumerate(List<int> values)
            {
                var sum = 0;
                foreach (var value in values) sum = sum * 7 + value;
                return sum;
            }

            public static int Run(int input)
            {
                var list = new List<int>();
                for (var i = 0; i < input + 3; i++) list.Add(i * i - input);
                return Julia(-0.8 + input * 0.01, 0.156) + (int)(Narrow(input) % 1_000_003) * 3 + Generic(input) * 5 +
                    Escaping(input) * 7 + Enumerate(list) % 100_000 * 11;
            }
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, -2026321 },
        { 1, -2974615 },
        { 2, 2634221 },
        { 5, -3034126 },
        { 13, -1437067 },
        { 40, 1207884 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3EmitsLessCodeThanWithWholeStructLocals(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"StructScalarReplacement_{target}", Source);

        var split = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var whole = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(StructScalarReplacement: ManagedStructScalarReplacement.Disabled));

        Assert.True(split.ApplicationModule.Length < whole.ApplicationModule.Length,
            $"split {split.ApplicationModule.Length} bytes, whole {whole.ApplicationModule.Length}");
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                split.ApplicationModule, assets.Directory, input, target, split.StaticDataEnd));
        }
        Assert.Equal(-2974615, ExecuteWithStandardWasiNode(
            whole.ApplicationModule, assets.Directory, 1, target, whole.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SplitStructLocalsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("StructScalarReplacementOz", Source);
        var result = Compile(assets, assembly, WasmTarget.Wasm32, FinalWasmOptimization.Oz, null);

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
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
}
