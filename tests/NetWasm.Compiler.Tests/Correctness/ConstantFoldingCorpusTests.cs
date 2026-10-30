using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// sizeof(T), Unsafe.SizeOf<T>() and Vector<T>.Count are constants once T is
// closed, so upstream's `if (sizeof(T) == 2)` chains fold as the JIT folds them:
// arithmetic, comparisons and two-operand branches on constants fold, the code
// they cut off goes, and a jump across it falls through so a constant reaches
// the comparison after a folded check. Folding never removes a throw: a
// constant division by zero or int.MinValue / -1 still throws, and shift
// counts wrap as Wasm's do.
[Collection(CorrectnessTestGroup.Name)]
public sealed class ConstantFoldingCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.ConstantFolding;

        public struct Pair
        {
            public long A;
            public int B;
        }

        public static unsafe class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => Shape<byte>() * 1000 + Shape<short>() * 100 + Shape<int>() * 10 + Shape<long>(),
                1 => Shape<char>() * 1000 + Shape<float>() * 100 + Shape<double>() * 10 + Shape<Pair>(),
                2 => Lanes<byte>() * 10000 + Lanes<ushort>() * 1000 + Lanes<int>() * 100 + Lanes<double>(),
                3 => Arithmetic<int>() ^ Arithmetic<byte>() * 7,
                4 => Guarded<int>(),
                5 => Overflowing<int>(),
                6 => Shifted<int>() * 31 + Shifted<long>(),
                7 => Unsigned<int>() * 2 + Unsigned<byte>(),
                _ => Checked<sbyte>(input) + Checked<int>(input) * 3,
            };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Shape<T>() where T : unmanaged
            {
                if (sizeof(T) == 1) return 1;
                if (sizeof(T) == 2) return 2;
                if (Unsafe.SizeOf<T>() == 4) return typeof(T) == typeof(float) ? 5 : 4;
                if (sizeof(T) == 8) return 8;
                return sizeof(T);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lanes<T>() where T : unmanaged =>
                Vector<T>.Count * 16 / sizeof(Vector<T>) + (sizeof(T) > Vector<byte>.Count ? 1000 : 0);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Arithmetic<T>() where T : unmanaged
            {
                var size = sizeof(T);
                return (size * 37 - 3) / 2 + (size - 9) % 4 + (-size & 0xff) + (~size | 3) + (size << 3) - ((uint)(size - 6) / 3 > 1 ? 1 : 0);
            }

            // size - 4 is 0 for int: the division throws.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Guarded<T>() where T : unmanaged
            {
                try
                {
                    return sizeof(T) * 2 / (sizeof(T) - 4);
                }
                catch (DivideByZeroException)
                {
                    return -1;
                }
            }

            // int.MinValue / -1 throws.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Overflowing<T>() where T : unmanaged
            {
                try
                {
                    return (int.MinValue + sizeof(T) - 4) / (sizeof(T) - 5);
                }
                catch (ArithmeticException exception)
                {
                    return exception is OverflowException ? -2 : -3;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Shifted<T>() where T : unmanaged =>
                (1 << (sizeof(T) * 8 + 1)) + (-64 >> (sizeof(T) * 8 + 2)) + (int)(0xFFFFFFC0u >> (sizeof(T) + 1));

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Unsigned<T>() where T : unmanaged
            {
                var count = 0;
                if ((uint)(sizeof(T) - 8) > 3) count++;
                if ((uint)(sizeof(T) - 8) >= 3) count += 2;
                if ((uint)sizeof(T) < (uint)(sizeof(T) - 10)) count += 4;
                if (sizeof(T) - 8 < 0) count += 8;
                return count;
            }

            // A branch on a constant guards a non-constant result.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Checked<T>(int value) where T : unmanaged
            {
                if (sizeof(T) > 2 && value > 3) return value * sizeof(T);
                return sizeof(T) == 1 ? -value : value + 1;
            }

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void ConstantFoldingMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ConstantFoldingCorpus",
            "NetWasm.Correctness.ConstantFolding",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            AllowUnsafe = true,
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    // The size-dependent methods keep no sizeof, call or branch once
    // specialized: each closed Shape<T> and Lanes<T> returns its constant.
    [Fact]
    public void SpecializedSizeChecksFoldAway()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("ConstantFolding", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.ConstantFolding.EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3));

        // Pair's size comes from its layout, so Shape<Pair> keeps its sizeof.
        var shapes = result.Program.ConstructedMethods.Values
            .Where(static method => method.Method.Definition.Name is "Shape" or "Lanes" &&
                method.Method.MethodArguments[0].FullName != "NetWasm.Correctness.ConstantFolding.Pair")
            .ToArray();
        Assert.Equal(11, shapes.Length);
        foreach (var shape in shapes)
        {
            var live = shape.Body.Instructions.Where(static instruction => instruction.Operation != CilOperation.Nop).ToArray();
            Assert.DoesNotContain(live, static instruction => instruction.Operation is CilOperation.SizeOf or
                CilOperation.Call or CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or CilOperation.BranchIfEqual or
                CilOperation.BranchIfNotEqual or CilOperation.BranchIfGreaterThanSigned);
        }
    }
}
