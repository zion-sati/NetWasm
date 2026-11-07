using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// ECMA-335 III.1.6 lets an argument, a store to a local, argument or field, and a
// return take an int32 where a native int is declared and the reverse. C# relies on it
// for a multidimensional array's indices and lengths of other integer types: it widens
// them to native int and calls the array's int32 accessors (issue #196). Each program
// gives desktop .NET 11's result on Wasm32 and Wasm64, where native int is 64-bit.
public sealed class ImplicitArgumentCoercionCompilationTests
{
    private const string ArraySource = """
            using System;

            public static class EntryPoint
            {
                // Read, write, compound-assign and take a reference with each index type C# lowers
                // through the array's int32 accessors: nint, nuint, long, ulong and uint.
                private static int Indices(int input)
                {
                    var grid = new int[5, 4];
                    nint n = 1 + (input & 1);
                    nuint u = (nuint)(2 + (input & 1));
                    long l = 3;
                    ulong w = (ulong)(input & 3);
                    uint i = 1;
                    grid[n, 1] = input;
                    grid[u, 2] = input * 3;
                    grid[l, w] += 7;
                    grid[i, 3]++;
                    ref var cell = ref grid[n, 0];
                    cell = input + 11;
                    return grid[n, 1] + grid[u, 2] * 5 + grid[l, w] * 7 + grid[i, 3] * 11 + grid[n, 0] * 13 + grid[(int)n, 1] * 17;
                }

                // Lengths of each type, a three-dimensional array, and an index chosen on two paths.
                private static int Lengths(int input)
                {
                    nint rows = 3 + (input & 1);
                    long columns = 2;
                    uint depth = 2;
                    var cube = new long[rows, columns, depth];
                    nint first = 1, second = 2;
                    cube[input > 0 ? first : second, 1, depth - 1] = input + 5;
                    cube[rows - 1, columns - 1, 0] = 9;
                    return (int)(cube.Length * 100 + cube.GetLength(0) * 10 + cube[input > 0 ? 1 : 2, 1, 1] + cube[rows - 1, 1, 0]);
                }

                // Out of range indices and lengths throw as with int32 ones.
                private static int Failures(int input)
                {
                    var grid = new int[2, 2];
                    var result = 0;
                    try { nint n = input - 10; result += grid[n, 0]; } catch (IndexOutOfRangeException) { result += 1; }
                    try { long l = 2 + (input & 1); grid[l, 1] = 4; } catch (IndexOutOfRangeException) { result += 10; }
                    try { nint rows = input - 10; result += new int[rows, 1].Length; } catch (OverflowException) { result += 100; }
                    return result;
                }

                public static int Run(int input)
                {
                    long hash = 17;
                    foreach (var value in new[] { Indices(input), Lengths(input), Failures(input) })
                    {
                        hash = hash * 31 + value;
                    }
                    return (int)(hash ^ (hash >> 32));
                }
            }
        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] ArrayCases =
        [(0, 740205), (1, 797152), (3, 885626), (7, 1062574), (-4, 563257)];

    // As desktop .NET 11 computes them for ImplicitCoercionAssemblyBuilder's assembly.
    private static readonly (int Input, int Expected)[] PositionCases =
        [(0, -584969740), (1, -767689099), (3, -1133127817), (7, -1615771087), (-4, 145908658)];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void RectangularArraysTakeIndicesAndLengthsOfEveryIntegerType(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        Run(assets, assets.CompileOptimizedSource("CoercedArrayIndices_" + target + optimization, ArraySource),
            target, optimization, ArrayCases);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void EveryPositionSignExtendsOrTruncatesTheValue(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "ImplicitCoercions_" + target + optimization + ".dll");
        File.WriteAllBytes(assembly, ImplicitCoercionAssemblyBuilder.Build());
        Run(assets, assembly, target, optimization, PositionCases,
            ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"));
    }

    private static void Run(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        FinalWasmOptimization optimization,
        (int Input, int Expected)[] cases,
        ImmutableDictionary<string, string>? aliases = null)
    {
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: aliases,
            ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        foreach (var (input, expected) in cases)
        {
            Assert.Equal(expected, CompilerTestSupport.ExecuteWithStandardWasiNode(
                compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd));
        }
    }
}
