using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StructuralComparisonsContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StructuralDelegationMatchesDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "StructuralFixture", Source, "StructuralFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        namespace StructuralFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var eq = StructuralComparisons.StructuralEqualityComparer;
                var cmp = StructuralComparisons.StructuralComparer;
                if (!ReferenceEquals(eq, StructuralComparisons.StructuralEqualityComparer) || !ReferenceEquals(cmp, StructuralComparisons.StructuralComparer)) return 1;
                if (!eq.Equals(null, null) || eq.Equals(null, 1) || eq.Equals(1, null) || eq.GetHashCode(null) != 0) return 2;
                if (cmp.Compare(null, null) != 0 || cmp.Compare(null, 1) >= 0 || cmp.Compare(1, null) <= 0) return 3;
                if (!eq.Equals(42, 42) || eq.Equals(42, 43) || eq.GetHashCode(42) != 42.GetHashCode()) return 4;
                if (cmp.Compare(41, 42) >= 0 || cmp.Compare(42, 41) <= 0) return 5;
                var a = new object[] { 1, new int[] { 2, 3 }, null };
                var b = new object[] { 1, new int[] { 2, 3 }, null };
                var c = new object[] { 1, new int[] { 2, 4 }, null };
                if (!eq.Equals(a, b) || eq.Equals(a, c) || eq.GetHashCode(a) != eq.GetHashCode(b)) return 6;
                if (cmp.Compare(a, b) != 0 || cmp.Compare(a, c) >= 0 || cmp.Compare(c, a) <= 0) return 7;
                if (eq.Equals(new int[] { 1 }, new int[] { 1, 2 })) return 8;
                try { cmp.Compare(new int[] { 1 }, new int[] { 1, 2 }); return 9; } catch (ArgumentException) { }
                var t1 = Tuple.Create(1, new int[] { 2, 3 }); var t2 = Tuple.Create(1, new int[] { 2, 3 });
                if (!eq.Equals(t1, t2) || cmp.Compare(t1, t2) != 0 || eq.GetHashCode(t1) != eq.GetHashCode(t2)) return 10;
                var v1 = (1, new int[] { 2, 3 }); var v2 = (1, new int[] { 2, 3 });
                if (!eq.Equals(v1, v2) || cmp.Compare(v1, v2) != 0 || eq.GetHashCode(v1) != eq.GetHashCode(v2)) return 11;
                var custom = new Custom();
                if (!eq.Equals(custom, 42) || eq.GetHashCode(custom) != 42 || cmp.Compare(custom, 42) != 0) return 12;
                return input + 1;
            }
        }
        public sealed class Custom : IStructuralEquatable, IStructuralComparable
        {
            public bool Equals(object other, IEqualityComparer comparer) => comparer.Equals(42, other);
            public int GetHashCode(IEqualityComparer comparer) => comparer.GetHashCode(42);
            public int CompareTo(object other, IComparer comparer) => comparer.Compare(42, other);
        }
        """;
}
