using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// MoveNext and Dispose are declared on IEnumerator and IDisposable, so every
// enumerator and disposable in a program could receive them. A foreach keeps
// its enumerator in a local of type IEnumerator<T>, which bounds the call
// site to enumerators of T: iterating ints never dispatches to a string
// enumerator, and its dispatch code does not test for one.
public sealed class DispatchReceiverBoundCompilationTests
{
    private const string Source = """
        public static class EntryPoint
        {
            private static int Sum(System.Collections.Generic.IEnumerable<int> values)
            {
                var sum = 0;
                foreach (var value in values) sum += value;
                return sum;
            }

            private static int Length(System.Collections.Generic.IEnumerable<string> values)
            {
                var length = 0;
                foreach (var value in values) length += value.Length;
                return length;
            }

            private static System.Collections.Generic.IEnumerable<int> Numbers(int count)
            {
                for (var index = 0; index < count; index++) yield return index;
            }

            private static System.Collections.Generic.IEnumerable<string> Words(int count)
            {
                for (var index = 0; index < count; index++) yield return new string('w', index + 1);
            }

            public static int Run(int input) =>
                Sum(Numbers(input + 3)) * 1000 + Sum(new System.Collections.Generic.List<int> { 1, 2, input }) * 100 +
                Length(Words(input + 2)) * 10 + Length(new[] { "a", "bc" });
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    public void ForeachDispatchesOnlyToEnumeratorsOfItsElementType(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DispatchReceiverBounds", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var enumeratorSites = result.Program.DispatchCallSites.Values
            .Where(static site => site.Declaration.Definition.Name is "MoveNext" or "Dispose" &&
                site.Targets.Any(static target => IsIterator(target, "Numbers") || IsIterator(target, "Words")))
            .ToArray();
        Assert.NotEmpty(enumeratorSites);
        Assert.All(enumeratorSites, static site => Assert.False(
            site.Targets.Any(static target => IsIterator(target, "Numbers")) &&
            site.Targets.Any(static target => IsIterator(target, "Words")),
            $"{site.Key} dispatches to the iterators of both element types."));

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 3333), (1, 6463), (7, 46453) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static bool IsIterator(DispatchTargetModel target, string method) =>
        target.ReceiverType.CanonicalName.Contains($"<{method}>", StringComparison.Ordinal);
}
