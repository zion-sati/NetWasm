using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Brief item 1: the branches an unwritten reference-typed static field decides fold before the program is built
// again, so a type reached only behind them is neither constructed nor allocated, and is not registered, at O2 and Oz.
// O0 does not fold unwritten fields, so there it is. The field is tested with `is not null`, `!= null`, `?.` and `??`.
public sealed class UnwrittenReferenceFieldBranchFoldingTests
{
    private const string Source = """
        public sealed class Holder
        {
            public string Name = "holder";
        }

        public sealed class RareByPattern
        {
            public int Value = 7;
        }

        public sealed class RareByComparison
        {
            public int Value = 9;
        }

        public sealed class RareByInteger
        {
            public int Value = 11;
        }

        public static class EntryPoint
        {
            private static Holder s_holder;
            private static int s_count;

            public static int Run(int input)
            {
                if (s_count != 0) return new RareByInteger().Value;
                if (s_holder is not null) return new RareByPattern().Value;
                if (s_holder != null) return new RareByComparison().Value;
                var name = s_holder?.Name ?? "none";
                return input + name.Length - 3;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O0, true)]
    [InlineData(FinalWasmOptimization.O2, false)]
    [InlineData(FinalWasmOptimization.Oz, false)]
    public void ATypeReachedOnlyBehindTheFieldsBranchesIsNotInTheProgram(FinalWasmOptimization optimization, bool reached)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UnwrittenReferenceBranch", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32, Optimization: optimization));

        var types = result.Program.ConstructedTypes.Concat(result.Program.AllocatedTypes)
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(reached, types.Contains("RareByPattern"));
        Assert.Equal(reached, types.Contains("RareByComparison"));
        Assert.DoesNotContain("Holder", types);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
