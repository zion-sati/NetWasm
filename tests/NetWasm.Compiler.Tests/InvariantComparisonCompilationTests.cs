using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class InvariantComparisonCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InvariantComparisonMatchesRuntimeProfile(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "InvariantComparisonFixture", Source, "InvariantComparisonFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        namespace InvariantComparisonFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var info = CultureInfo.InvariantCulture.CompareInfo;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                if (CultureInfo.CurrentUICulture.Name != "" || info.Name != "") return 1;
                var flags = new[] { CompareOptions.None, CompareOptions.IgnoreCase,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace,
                    CompareOptions.IgnoreSymbols | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType,
                    CompareOptions.StringSort, (CompareOptions)32,
                    CompareOptions.Ordinal, CompareOptions.OrdinalIgnoreCase };
                var left = new[] { "a", "é", "Ω", "𐐀", "a-b", "2", "a\0b", "\uD800" };
                var right = new[] { "A", "e", "ω", "𐐨", "ab", "10", "ab", "\uD801" };
                foreach (var flag in flags)
                {
                    var mode = flag == CompareOptions.OrdinalIgnoreCase || (flag & CompareOptions.IgnoreCase) != 0
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    for (var i = 0; i < left.Length; i++)
                    {
                        var expected = Math.Sign(string.Compare(left[i], right[i], mode));
                        if (Math.Sign(info.Compare(left[i], right[i], flag)) != expected) return 2;
                        if (Math.Sign(info.Compare(left[i].AsSpan(), right[i].AsSpan(), flag)) != expected) return 3;
                        if (info.GetHashCode(left[i], flag) != info.GetHashCode(left[i].AsSpan(), flag)) return 4;
                        if (expected == 0 && info.GetHashCode(left[i], flag) != info.GetHashCode(right[i], flag)) return 5;
                    }
                    if (info.Compare(null, null, flag) != 0 || info.Compare(null, "", flag) >= 0 || info.Compare("", null, flag) <= 0) return 6;
                }
                var comparer = info.GetStringComparer(CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                var equivalent = info.GetStringComparer(CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase) || !comparer.Equals((object)equivalent) || comparer.GetHashCode() != equivalent.GetHashCode()) return 15;
                if (comparer.Equals((object)info.GetStringComparer(CompareOptions.IgnoreCase))) return 16;
                if (!ReferenceEquals(info.GetStringComparer(CompareOptions.Ordinal), StringComparer.Ordinal)) return 17;
                if (!ReferenceEquals(info.GetStringComparer(CompareOptions.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)) return 18;
                var dictionary = new System.Collections.Generic.Dictionary<string, int>(comparer);
                dictionary.Add("Ω", 42);
                if (dictionary["ω"] != 42 || comparer.Equals("é", "e")) return 19;
                if (!comparer.Equals(null, null) || comparer.Equals(null, "") || comparer.Equals("", null)) return 20;
                if (comparer.Compare(null, null) != 0 || comparer.Compare(null, "") >= 0 || comparer.Compare("", null) <= 0) return 21;
                var alternate = (System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<char>, string>)comparer;
                if (alternate.Equals(ReadOnlySpan<char>.Empty, null) || !alternate.Equals("ω".AsSpan(), "Ω")) return 22;
                if (alternate.GetHashCode("ω".AsSpan()) != comparer.GetHashCode("Ω") || alternate.Create("abc".AsSpan()) != "abc") return 23;
                try { comparer.GetHashCode(null); return 24; }
                catch (ArgumentNullException error) { if (error.ParamName != "obj") return 25; }
                try { GlobalizationExtensions.GetStringComparer(null, CompareOptions.None); return 26; }
                catch (ArgumentNullException error) { if (error.ParamName != "compareInfo") return 27; }
                if (info.Compare("a", "a") != 0) return 7;
                foreach (var invalid in new[] { (CompareOptions)(-1), CompareOptions.Ordinal | CompareOptions.IgnoreCase,
                    CompareOptions.OrdinalIgnoreCase | CompareOptions.IgnoreNonSpace })
                {
                    try { info.GetStringComparer(invalid); return 28; }
                    catch (ArgumentException error) { if (error.ParamName != "options") return 29; }
                    try { info.Compare(null, null, invalid); return 8; }
                    catch (ArgumentException error) { if (error.ParamName != "options") return 9; }
                    try { info.GetHashCode("", invalid); return 10; }
                    catch (ArgumentException error) { if (error.ParamName != "options") return 11; }
                }
                try { info.GetHashCode((string)null, CompareOptions.None); return 12; }
                catch (ArgumentNullException error) { if (error.ParamName != "source") return 13; }
                try { CultureInfo.CurrentUICulture = null; return 14; }
                catch (ArgumentNullException) { }
                return 42;
            }
        }
        """;
}
