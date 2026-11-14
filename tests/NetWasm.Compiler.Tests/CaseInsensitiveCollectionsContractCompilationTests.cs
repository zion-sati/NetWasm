using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CaseInsensitiveCollectionsContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InvariantComparisonAndHashingMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "CaseFixture", Source, "CaseFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable CS0618
        using System;
        using System.Collections;
        using System.Globalization;
        namespace CaseFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var comparers = new CaseInsensitiveComparer[] { new(), new(CultureInfo.InvariantCulture), CaseInsensitiveComparer.Default, CaseInsensitiveComparer.DefaultInvariant };
                var hashes = new CaseInsensitiveHashCodeProvider[] { new(), new(CultureInfo.InvariantCulture), CaseInsensitiveHashCodeProvider.Default, CaseInsensitiveHashCodeProvider.DefaultInvariant };
                for (var i = 0; i < comparers.Length; i++)
                {
                    var c = comparers[i]; var h = hashes[i];
                    if (c.Compare("AbC", "aBc") != 0 || c.Compare("a", "b") >= 0 || c.Compare("b", "a") <= 0) return 1;
                    if (c.Compare(null, null) != 0 || c.Compare(null, "a") >= 0 || c.Compare("a", null) <= 0) return 2;
                    if (c.Compare(1, 2) >= 0 || c.Compare(42, 42) != 0) return 3;
                    if (h.GetHashCode("AbC") != h.GetHashCode("aBc") || h.GetHashCode(42) != 42.GetHashCode()) return 4;
                    try { h.GetHashCode(null); return 5; } catch (ArgumentNullException) { }
                    try { c.Compare(new object(), new object()); return 6; } catch (ArgumentException) { }
                }
                if (!ReferenceEquals(CaseInsensitiveComparer.DefaultInvariant, CaseInsensitiveComparer.DefaultInvariant)) return 7;
                try { new CaseInsensitiveComparer(null); return 8; } catch (ArgumentNullException) { }
                try { new CaseInsensitiveHashCodeProvider(null); return 9; } catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        """;
}
