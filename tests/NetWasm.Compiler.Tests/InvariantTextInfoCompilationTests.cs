using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class InvariantTextInfoCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InvariantTextInfoPreservesMutabilityAndParentContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "InvariantTextFixture", Source, "InvariantTextFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        namespace InvariantTextFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var invariant = CultureInfo.InvariantCulture;
                if (!ReferenceEquals(invariant.Parent, invariant)) return 1;
                var culture = new CultureInfo("");
                if (!ReferenceEquals(culture.Parent, invariant)) return 2;
                var info = invariant.TextInfo;
                if (!info.IsReadOnly || info.CultureName != "" || info.ListSeparator != ",") return 3;
                if (!ReferenceEquals(info, invariant.TextInfo) || !ReferenceEquals(info, TextInfo.ReadOnly(info))) return 4;
                try { info.ListSeparator = ";"; return 5; }
                catch (InvalidOperationException) { }
                try { info.ListSeparator = null; return 6; }
                catch (ArgumentNullException e) { if (e.ParamName != "value") return 7; }
                var mutable = culture.TextInfo;
                if (mutable.IsReadOnly || !ReferenceEquals(mutable, culture.TextInfo)) return 8;
                mutable.ListSeparator = "::";
                var frozen = TextInfo.ReadOnly(mutable);
                if (!frozen.IsReadOnly || frozen.ListSeparator != "::" || ReferenceEquals(frozen, mutable)) return 9;
                mutable.ListSeparator = "";
                if (frozen.ListSeparator != "::" || info.ListSeparator != ",") return 10;
                var copy = (TextInfo)((ICloneable)frozen).Clone();
                if (copy.IsReadOnly || copy.ListSeparator != "::") return 11;
                copy.ListSeparator = "|";
                if (frozen.ListSeparator != "::" || !copy.Equals(info) || copy.GetHashCode() != info.GetHashCode()) return 12;
                if (copy.Equals(null) || copy.Equals("") || copy.ToString() != "TextInfo - ") return 13;
                try { TextInfo.ReadOnly(null); return 14; }
                catch (ArgumentNullException e) { if (e.ParamName != "textInfo") return 15; }
                return input + 1;
            }
        }
        """;
}
