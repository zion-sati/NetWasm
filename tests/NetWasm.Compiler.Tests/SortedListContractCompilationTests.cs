using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SortedListContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SortingViewsAndFactoriesMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SortedFixture", Source, "SortedFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        using System.Globalization;
        namespace SortedFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                foreach (var comparer in new StringComparer[] { StringComparer.CurrentCultureIgnoreCase, StringComparer.InvariantCultureIgnoreCase, StringComparer.Create(CultureInfo.InvariantCulture, true), StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase), StringComparer.FromComparison(StringComparison.InvariantCultureIgnoreCase) })
                {
                    if (!comparer.Equals("ABC", "abc") || comparer.Compare("ABC", "abc") != 0 || comparer.GetHashCode("ABC") != comparer.GetHashCode("abc")) return 19;
                    if (comparer.Compare(null, "a") >= 0 || comparer.Compare("a", null) <= 0 || comparer.Equals(null, "a") || !comparer.Equals(null, null)) return 20;
                    if (!comparer.Equals((object)StringComparer.InvariantCultureIgnoreCase) || comparer.GetHashCode() != StringComparer.InvariantCultureIgnoreCase.GetHashCode()) return 21;
                    try { comparer.GetHashCode((string)null); return 22; } catch (ArgumentNullException) { }
                }
                if (StringComparer.InvariantCulture.Equals("a", "A") || StringComparer.CurrentCulture.Equals("a", "A")) return 23;
                if (!StringComparer.FromComparison(StringComparison.CurrentCulture).Equals((object)StringComparer.InvariantCulture)) return 24;
                try { StringComparer.Create(null, true); return 25; } catch (ArgumentNullException) { }
                try { StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.Ordinal); return 26; } catch (ArgumentException) { }
                try { StringComparer.FromComparison((StringComparison)99); return 27; } catch (ArgumentException) { }
                var map = new SortedList(1);
                for (var i = 19; i >= 0; i--) map.Add(i, i * 2);
                var keys = map.GetKeyList(); var values = map.GetValueList();
                for (var i = 0; i < 20; i++) if ((int)map.GetKey(i) != i || (int)map.GetByIndex(i) != i * 2) return 1;
                if (map.IndexOfKey(10) != 10 || map.IndexOfValue(20) != 10 || map.IndexOfKey(50) != -1) return 2;
                map.SetByIndex(0, null); if (!map.ContainsValue(null) || map.GetByIndex(0) != null) return 3;
                try { map.Add(1, 9); return 4; } catch (ArgumentException) { }
                var clone = (SortedList)map.Clone(); clone.Remove(1); if (map.Count != 20 || clone.Count != 19) return 5;
                var copied = new DictionaryEntry[20]; map.CopyTo(copied, 0);
                for (var i = 0; i < 20; i++) if ((int)copied[i].Key != i) return 6;
                var e = map.GetEnumerator(); e.MoveNext(); map[1] = 50;
                try { e.MoveNext(); return 7; } catch (InvalidOperationException) { }
                map.RemoveAt(0); if ((int)keys[0] != 1 || (int)values[0] != 50 || keys.Count != 19) return 8;
                try { keys.Add(42); return 9; } catch (NotSupportedException) { }
                try { values[0] = 42; return 10; } catch (NotSupportedException) { }
                map.TrimToSize(); if (map.Capacity != map.Count) return 11;
                var sync = SortedList.Synchronized(map); sync.Add(30, 60);
                if (!sync.IsSynchronized || !ReferenceEquals(sync.SyncRoot, map.SyncRoot) || !map.ContainsKey(30)) return 12;
                var source = new Hashtable(); source.Add(3, "c"); source.Add(1, "a");
                var from = new SortedList(source); if ((int)from.GetKey(0) != 1 || (string)from.GetByIndex(1) != "c") return 13;
                var ci = CollectionsUtil.CreateCaseInsensitiveSortedList(); ci.Add("B", 2); ci.Add("a", 1);
                if ((string)ci.GetKey(0) != "a" || (int)ci["b"] != 2) return 14;
                foreach (var table in new Hashtable[] { CollectionsUtil.CreateCaseInsensitiveHashtable(), CollectionsUtil.CreateCaseInsensitiveHashtable(4), CollectionsUtil.CreateCaseInsensitiveHashtable(new Hashtable()) })
                { table.Add("KEY", 42); if ((int)table["key"] != 42) return 15; }
                try { map.Add(null, 1); return 16; } catch (ArgumentNullException) { }
                try { map.Capacity = 0; return 17; } catch (ArgumentOutOfRangeException) { }
                map.Clear(); if (keys.Count != 0 || values.Count != 0) return 18;
                return input + 1;
            }
        }
        """;
}
