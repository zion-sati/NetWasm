using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A counted loop that fills or copies the same stack blocks on every pass runs
// its body once: one or several fills, a fill at a constant offset, and a copy
// from a block the loop never writes. A loop whose counter decides the value
// or is read afterwards, one that swaps two blocks, one that may not run, one
// whose counter another path starts past the bound, and one that passes a
// written block to a call keep running every pass.
public sealed class IdempotentLoopCollapseCompilationTests
{
    private const string Source = """
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public static unsafe class EntryPoint
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct Block
            {
                public long A;
                public long B;
                public long C;
                public long D;
            }

            private static int s_touched;

            private static int Hash(in Block block) =>
                (int)(block.A ^ block.B * 3 ^ block.C * 5 ^ block.D * 7 ^ (block.A >> 29) ^ (block.D >> 31));

            private static Block Seeded(int seed)
            {
                Block block;
                block.A = seed;
                block.B = seed * 3L + 1;
                block.C = ~seed;
                block.D = seed * 0x10001L;
                return block;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Touch(Block* block) => s_touched += (int)block->A & 0xff;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Fill(int seed)
            {
                Unsafe.SkipInit(out Block block);
                for (var i = 0; i < 100; i++)
                    Unsafe.InitBlock(&block, (byte)seed, 32);
                return Hash(block);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Copy(int seed)
            {
                var source = Seeded(seed);
                Unsafe.SkipInit(out Block target);
                for (var i = 0; i < 10; i++)
                    Unsafe.CopyBlock(&target, &source, 32);
                return Hash(target);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoBlocks(int seed)
            {
                var source = Seeded(seed);
                Unsafe.SkipInit(out Block filled);
                Unsafe.SkipInit(out Block copied);
                for (var i = 3; i < 9; i += 2)
                {
                    Unsafe.InitBlock(&filled, (byte)(seed * 7), 16);
                    Unsafe.InitBlock((byte*)&filled + 16, 0x5a, 16);
                    Unsafe.CopyBlock(&copied, &source, 32);
                }
                return Hash(filled) * 31 + Hash(copied);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterValue(int seed)
            {
                Unsafe.SkipInit(out Block block);
                for (var i = 0; i < 7; i++)
                    Unsafe.InitBlock(&block, (byte)(seed + i), 32);
                return Hash(block);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterAfter(int seed)
            {
                Unsafe.SkipInit(out Block block);
                int i;
                for (i = 0; i < 5; i++)
                    Unsafe.InitBlock(&block, (byte)seed, 32);
                return Hash(block) + i;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Swap(int seed)
            {
                var a = Seeded(seed);
                var b = Seeded(seed + 1);
                Unsafe.SkipInit(out Block t);
                for (var i = 0; i < 2; i++)
                {
                    Unsafe.CopyBlock(&t, &a, 32);
                    Unsafe.CopyBlock(&a, &b, 32);
                    Unsafe.CopyBlock(&b, &t, 32);
                }
                return Hash(a) * 31 + Hash(b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ZeroTrip(int seed)
            {
                var block = Seeded(seed);
                for (var i = 4; i < 4; i++)
                    Unsafe.InitBlock(&block, 0xff, 32);
                return Hash(block);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int VariableBound(int seed)
            {
                var block = Seeded(seed);
                for (var i = 0; i < seed % 3; i++)
                    Unsafe.InitBlock(&block, 0xff, 32);
                return Hash(block);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Escapes(int seed)
            {
                Unsafe.SkipInit(out Block block);
                s_touched = 0;
                for (var i = 0; i < 6; i++)
                {
                    Unsafe.InitBlock(&block, (byte)seed, 32);
                    Touch(&block);
                }
                return Hash(block) + s_touched;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ConditionalStart(int seed)
            {
                var block = Seeded(seed);
                for (var i = seed > 100 ? 0 : 50; i < 10; i++)
                    Unsafe.InitBlock(&block, 0xa5, 32);
                return Hash(block);
            }

            public static int Run(int input) => (input / 1000) switch
            {
                0 => Fill(input % 1000),
                1 => Copy(input % 1000),
                2 => TwoBlocks(input % 1000),
                3 => CounterValue(input % 1000),
                4 => CounterAfter(input % 1000),
                5 => Swap(input % 1000),
                6 => ZeroTrip(input % 1000),
                7 => VariableBound(input % 1000),
                8 => Escapes(input % 1000),
                _ => ConditionalStart(input % 1000),
            };
        }
        """;

    private static readonly string[] Collapsible = ["Copy", "Fill", "TwoBlocks"];

    public static TheoryData<int, int> Cases => new()
    {
        { 3, 235802126 },
        { 200, -741092393 },
        { 1003, -1376284 },
        { 1301, -138085996 },
        { 2003, 604971682 },
        { 2200, -664413387 },
        { 3003, 1515870810 },
        { 3301, -858993457 },
        { 4003, 235802131 },
        { 4200, -741092388 },
        { 5003, -44499852 },
        { 5200, 1358484740 },
        { 6003, -1376284 },
        { 6301, -138085996 },
        { 7003, -1376284 },
        { 7200, 0 },
        { 8003, 235802144 },
        { 8301, 1145324881 },
        { 9003, -1376284 },
        { 9200, -2004318058 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3RunsOnlyRepeatingLoopsOnce(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource($"IdempotentLoopCollapse_{target}", Source);

        var collapsed = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var kept = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(IdempotentLoopCollapse: ManagedIdempotentLoopCollapse.Disabled));

        var looping = Looping(kept);
        Assert.Empty(Collapsible.Except(looping));
        Assert.Equal(looping.Except(Collapsible), Looping(collapsed));
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                collapsed.ApplicationModule, assets.Directory, input, target, collapsed.StaticDataEnd));
        }
        Assert.Equal(604971682, ExecuteWithStandardWasiNode(
            kept.ApplicationModule, assets.Directory, 2003, target, kept.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CollapsedLoopsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("IdempotentLoopCollapseOz", Source);
        var result = Compile(assets, assembly, WasmTarget.Wasm32, FinalWasmOptimization.Oz, null);

        Assert.DoesNotContain("Fill", Looping(result));
        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // The EntryPoint methods left with a branch back to an earlier instruction.
    private static string[] Looping(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "EntryPoint" &&
                method.Body.Instructions.Any(static instruction => instruction.Operand switch
                {
                    CilOperand.BranchTarget target => target.Offset <= instruction.Offset,
                    CilOperand.SwitchTargets targets => targets.Offsets.Any(offset => offset <= instruction.Offset),
                    _ => false,
                }))
            .Select(static method => method.Method.Definition.Name)
            .Order()];

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
