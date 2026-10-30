using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// An array initializer with field RVA data writes small arrays as constant stores, eight bytes at a
// time, and copies a larger one from its field's static data with one memory.copy. Every element
// type and size class reads back as desktop's does.
[Collection(CorrectnessTestGroup.Name)]
public sealed partial class ArrayInitializerCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;

        namespace NetWasm.Correctness.ArrayInitializers;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => Hash(new byte[] { 1, 2, 3 }),
                1 => Hash(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }),
                2 => Hash(new short[] { -1, 2, short.MinValue, 4, 5, short.MaxValue, 7 }),
                3 => Hash(new int[] { 1, -2, 3, int.MinValue, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }),
                4 => Hash(new long[] { 1L << 40, -2, 3, long.MaxValue, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }),
                5 => Hash(new double[] { 0.5, -1.25, double.MaxValue, 3.75, 1e-300, 6, 7, 8, 9, 10, 11 }),
                6 => Hash(new char[] { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', 'A', 'B', 'C', 'D', 'E', 'F', 'G' }),
                7 => Hash(new float[] { 1.5f, -2f, float.Epsilon, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 17f }),
                _ => Hash(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50 }),
            };

            private static int Hash<T>(T[] values)
            {
                var hash = values.Length;
                foreach (var value in values) hash = unchecked(hash * 31 + value!.GetHashCode());
                return hash;
            }

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void ArrayInitializersMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ArrayInitializerCorpus",
            "NetWasm.Correctness.ArrayInitializers",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LargeInitializersCopyFromStaticData(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayInitializers" + target, Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "NetWasm.Correctness.ArrayInitializers.EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.None));
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var run = Functions().Matches(printed.StandardOutput)
            .Single(static function => function.Value.StartsWith("(func $NetWasm.Correctness.ArrayInitializers.EntryPoint::Run ",
                StringComparison.Ordinal)).Value;

        // Five initializers exceed 64 bytes (the longs, doubles, chars, floats and uints) and are
        // copied; the others are stored as constants, at most one byte store each.
        Assert.Equal(5, Regex.Count(run, "memory.copy"));
        Assert.InRange(Regex.Count(run, "i32.store8"), 0, 4);
    }

    [GeneratedRegex(@"\(func \$\S+.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline)]
    private static partial Regex Functions();
}
