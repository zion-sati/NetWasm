using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A loop that walks a reference over n elements, as StrengthReduction's
// strength-reduced loops do, runs four elements at a time ahead of the loop
// when the vectorizer leaves it: fields of structs at every stride, read
// through the reference or a copy of the element, short elements, folds of
// every operation into an accumulator per element of a group, folds that
// cannot be split, and an element copy read after the loop match
// desktop for every count from 0, so every remainder and every walk too short
// for a group.
public sealed class WalkLoopUnrollerCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.WalkLoops;

        public struct S3 { public byte A, B, C; }
        public struct S8 { public int A, B; }
        public struct S12 { public int A, B, C; }
        public struct S29 { public byte A; public long B, C, D; public int E; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var size = 0; size <= 21; size++)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => Walk8(S8s(size)),
                        1 => Walk12(S12s(size)),
                        2 => Walk3(S3s(size)),
                        3 => Walk29(S29s(size)),
                        4 => WalkShorts(Shorts(size)),
                        5 => WalkCopies(S8s(size)),
                        6 => CopiedWalk8(S8s(size)),
                        7 => CopiedWalk12(S12s(size)),
                        8 => WalkFolds(S8s(size)),
                        9 => WalkUnsplitFolds(S8s(size)),
                        _ => throw new ArgumentOutOfRangeException(nameof(input)),
                    });
                }
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Walk8(S8[] input)
            {
                var result = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        result += p.A | 1;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return result;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Walk12(S12[] input)
            {
                int sum = 0, mix = 5;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        length--;
                        sum += p.B * 3 - p.C;
                        mix ^= p.A << 2;
                        p = ref Unsafe.Add(ref p, 1);
                    } while (length != 0);
                }
                return Mix(sum, mix);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Walk3(S3[] input)
            {
                var result = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        result += p.A | 1;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return result;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Walk29(S29[] input)
            {
                var result = 0L;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        result += (p.A | 1) + p.C;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return (int)(result ^ (result >> 32));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkShorts(short[] input)
            {
                var result = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        result += (short)(p | 1);
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return result;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkCopies(S8[] input)
            {
                int result = 0, last = -7;
                var length = input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        last = p.B;
                        result += last - p.A;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return Mix(result, last);
            }

            // StrengthReduction's own loops copy each element into a local.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopiedWalk8(S8[] input)
            {
                var result = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        S8 s = p;
                        result += s.A | 1;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return result;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopiedWalk12(S12[] input)
            {
                var result = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        S12 s = p;
                        result += s.A | 1;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return result;
            }

            // Folds of every operation, 32- and 64-bit, each into its own
            // accumulator per element of a group.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkFolds(S8[] input)
            {
                int sum = 0, all = -1, mix = 0;
                long wide = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        sum += p.A;
                        all &= p.A | 0x10;
                        mix ^= p.B << 1;
                        wide += p.A * 3L;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return Mix(Mix(Mix(sum, all), mix), (int)(wide ^ (wide >> 32)));
            }

            // A step that does not reassociate, s * 3 + x, and an accumulator
            // the body also reads elsewhere stay one accumulator; trace, which
            // folds the running sum, splits.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkUnsplitFolds(S8[] input)
            {
                int scaled = 1, sum = 0, trace = 0;
                var length = (uint)input.Length;
                if (length > 0)
                {
                    ref var p = ref input[0];
                    do
                    {
                        scaled = scaled * 3 + p.A;
                        sum += p.B;
                        trace ^= sum;
                        p = ref Unsafe.Add(ref p, 1);
                        length--;
                    } while (length != 0);
                }
                return Mix(Mix(scaled, sum), trace);
            }

            private static S3[] S3s(int size)
            {
                var data = new S3[size];
                for (var i = 0; i < size; i++) data[i] = new S3 { A = (byte)(i * 37), B = (byte)i, C = (byte)(i ^ 0x5A) };
                return data;
            }

            private static S8[] S8s(int size)
            {
                var data = new S8[size];
                for (var i = 0; i < size; i++) data[i] = new S8 { A = i * 7919 - 1000, B = i * 31 };
                return data;
            }

            private static S12[] S12s(int size)
            {
                var data = new S12[size];
                for (var i = 0; i < size; i++) data[i] = new S12 { A = i * 13, B = i - 50, C = i * i };
                return data;
            }

            private static S29[] S29s(int size)
            {
                var data = new S29[size];
                for (var i = 0; i < size; i++) data[i] = new S29 { A = (byte)(i * 11), B = i, C = i * 0x9E3779B97L, D = -i, E = i };
                return data;
            }

            private static short[] Shorts(int size)
            {
                var data = new short[size];
                for (var i = 0; i < size; i++) data[i] = (short)(i * 4099 - 30000);
                return data;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void WalkLoopsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "WalkLoopUnrollerCorpus",
            "NetWasm.Correctness.WalkLoops",
            Source,
            [.. Enumerable.Range(0, 10)])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryWalkRunsFourElementsAtATimeAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("WalkLoops" + target, Source);

        var unrolled = Compile(assets, assembly, target, null);
        var rolled = Compile(assets, assembly, target, new WasmOptimizationOverrides(LoopUnrolling: ManagedLoopUnrolling.Disabled));

        Assert.Equal(["CopiedWalk12", "CopiedWalk8", "Walk12", "Walk29", "Walk3", "Walk8", "WalkCopies", "WalkFolds", "WalkShorts",
            "WalkUnsplitFolds"], Unrolled(unrolled));
        // Each split fold adds three accumulators. scaled * 3 + x does not
        // reassociate and trace reads sum, so only trace splits there.
        Assert.Equal(12, AddedLocals(unrolled, rolled, "WalkFolds"));
        Assert.Equal(3, AddedLocals(unrolled, rolled, "WalkUnsplitFolds"));
        Assert.Equal(3, AddedLocals(unrolled, rolled, "CopiedWalk12"));
        Assert.Empty(Unrolled(rolled));
        foreach (var input in Enumerable.Range(0, 10))
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(rolled.ApplicationModule, assets.Directory, input, target, rolled.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(unrolled.ApplicationModule, assets.Directory, input, target, unrolled.StaticDataEnd));
        }
    }

    // The EntryPoint methods that advance a reference by three elements, as
    // the group's last element does.
    private static string[] Unrolled(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.WalkLoops.EntryPoint" &&
                method.Body.Instructions.Zip(method.Body.Instructions.Skip(1)).Any(static pair =>
                    pair.First.Operand is CilOperand.ConstantI4 { Value: 3 } &&
                    pair.Second.Operand is CilOperand.MethodInstance { Value.Definition.Name: "Add" }))
            .Select(static method => method.Method.Definition.Name)
            .Order(StringComparer.Ordinal)];

    private static int AddedLocals(CompilationResult unrolled, CompilationResult rolled, string name)
    {
        static int Locals(CompilationResult result, string name) => result.Program.Methods.Values
            .Single(method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.WalkLoops.EntryPoint" &&
                method.Method.Definition.Name == name).Body.Locals.Length;
        return Locals(unrolled, name) - Locals(rolled, name);
    }

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.WalkLoops.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
