using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// for loops over a few constant values run once per value in a row at O3:
// a scan of a vector's lanes that breaks and reads i after the loop, a sum
// whose bounds check throws partway, a continue under a <= bound, a return
// from inside, a loop in a try, and a negative start all match desktop, as
// do the loops left rolled: a counter the body changes, one whose address is
// taken, a try inside the body, nine trips, and a body too large to copy.
public sealed class ConstantTripLoopUnrollerCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.ConstantTrips;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var seed = 0; seed < 20; seed++)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => LaneScan(Lanes(seed)),
                        1 => Guard(() => SumFour(Values(seed % 6, seed))),
                        2 => SkipEven(seed - 7),
                        3 => Find(Values(3, seed), seed % 5),
                        4 => Guard(() => InTry(Values(seed % 5, seed))),
                        5 => FromNegative(seed),
                        6 => ChangedCounter(seed),
                        7 => AddressedCounter(seed),
                        8 => TryInBody(Values(seed % 4, seed)),
                        9 => NineTrips(seed),
                        _ => LargeBody(seed),
                    });
                }
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LaneScan(Vector<byte> bytes)
            {
                var vector64 = Vector.AsVectorInt64(bytes);
                long value = 0;
                var i = 0;
                for (; i < Vector<long>.Count; i++)
                {
                    value = vector64[i];
                    if (value == 0) continue;
                    break;
                }
                var flag = (ulong)(value ^ (value - 1));
                return i * 8 + (int)((flag * 0x0001020304050608ul) >> 57);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumFour(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < 4; i++) sum = sum * 3 + values[i] * (i + 1);
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SkipEven(int x)
            {
                var sum = 0;
                for (var i = 1; i <= 5; i++)
                {
                    if (i % 2 == 0) continue;
                    sum += i * x;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Find(int[] values, int key)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (values[i] % 5 == key) return i;
                    if (values[i] < 0) break;
                }
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InTry(int[] values)
            {
                var sum = 0;
                try
                {
                    for (var i = 0; i < 4; i++) sum = sum * 3 + values[i];
                }
                catch (IndexOutOfRangeException)
                {
                    sum = -sum;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FromNegative(int x)
            {
                int i;
                var product = 1;
                for (i = -2; i < 2; i++) product = product * (x + i) + i;
                return product * 7 + i;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChangedCounter(int x)
            {
                var sum = 0;
                for (var i = 0; i < 4; i++)
                {
                    if ((x & (1 << i)) != 0) i++;
                    sum = sum * 5 + i;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddressedCounter(int x)
            {
                var sum = 0;
                for (var i = 0; i < 4; i++) sum += Bump(ref i, x);
                return sum;
            }

            private static int Bump(ref int i, int x)
            {
                if (x % 3 == 0) i++;
                return i * x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TryInBody(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < 3; i++)
                {
                    try { sum += values[i]; }
                    catch (IndexOutOfRangeException) { sum -= 100; }
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NineTrips(int x)
            {
                var sum = 0;
                for (var i = 0; i < 9; i++) sum = sum * 3 + (x ^ i);
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LargeBody(int x)
            {
                var sum = 0;
                for (var i = 0; i < 8; i++)
                {
                    sum = sum * 31 + x * i;
                    sum ^= sum >> 3;
                    sum += (x - i) * (x + i);
                    sum ^= sum << 5;
                    sum += x % (i + 1);
                    sum ^= sum >> 7;
                    sum += (x | i) - (x & i);
                }
                return sum;
            }

            private static Vector<byte> Lanes(int seed)
            {
                var bytes = new byte[Vector<byte>.Count];
                if (seed % 7 != 6) bytes[(seed * 5) % bytes.Length] = (byte)(seed * 13 + 1);
                return new Vector<byte>(bytes);
            }

            private static int[] Values(int length, int seed)
            {
                var values = new int[length];
                for (var i = 0; i < length; i++) values[i] = (i * 7919 + seed * 31) % 97 - (seed % 3 == 0 && i == 1 ? 200 : 0);
                return values;
            }

            private static int Guard(Func<int> run)
            {
                try { return run(); }
                catch (IndexOutOfRangeException) { return -12345; }
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void ConstantTripLoopsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ConstantTripLoopUnrollerCorpus",
            "NetWasm.Correctness.ConstantTrips",
            Source,
            [.. Enumerable.Range(0, 11)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LoopsOverAFewConstantValuesUnrollAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ConstantTrips" + target, Source);

        var unrolled = Compile(assets, assembly, target, null);
        var rolled = Compile(assets, assembly, target, new WasmOptimizationOverrides(LoopUnrolling: ManagedLoopUnrolling.Disabled));

        string[] loops = ["AddressedCounter", "ChangedCounter", "Find", "FromNegative", "InTry", "LaneScan", "LargeBody",
            "NineTrips", "SkipEven", "SumFour", "TryInBody"];
        Assert.Equal(["AddressedCounter", "ChangedCounter", "LargeBody", "NineTrips", "TryInBody"], Looping(unrolled, loops));
        Assert.Equal(loops, Looping(rolled, loops));
    }

    // The methods named that still branch backwards.
    private static string[] Looping(CompilationResult result, string[] names) =>
        [.. result.Program.Methods.Values
            .Where(method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.ConstantTrips.EntryPoint" &&
                names.Contains(method.Method.Definition.Name) &&
                method.Body.Instructions.Any(static instruction =>
                    instruction.Operand is CilOperand.BranchTarget { Offset: var target } && target <= instruction.Offset))
            .Select(static method => method.Method.Definition.Name)
            .Order(StringComparer.Ordinal)];

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.ConstantTrips.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
