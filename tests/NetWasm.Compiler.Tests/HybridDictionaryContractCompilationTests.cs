using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class HybridDictionaryContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CutoverAndComparerContractsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "HybridFixture", Source, "HybridFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace HybridFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var empty = new HybridDictionary();
                if (empty.Count != 0 || empty["none"] != null || empty.Contains("none")) return 1;
                empty.Remove("none");
                try { empty.Contains(null); return 2; } catch (ArgumentNullException) { }
                try { var x = empty[null]; return 3; } catch (ArgumentNullException) { }
                try { empty.Remove(null); return 4; } catch (ArgumentNullException) { }
                if (empty.IsReadOnly || empty.IsFixedSize || empty.IsSynchronized || !ReferenceEquals(empty.SyncRoot, empty)) return 5;
                if (empty.Keys.Count != 0 || empty.Values.Count != 0 || empty.GetEnumerator().MoveNext()) return 6;
                var flags = new HybridDictionary(true); flags.Add("A", 1);
                if ((int)flags["a"] != 1) return 7;
                var sized = new HybridDictionary(16); sized.Add(42, "answer");
                if ((string)sized[42] != "answer") return 8;
                for (var configuration = 0; configuration < 4; configuration++)
                {
                    var ignoreCase = configuration % 2 == 1;
                    var map = new HybridDictionary(configuration < 2 ? 0 : 16, ignoreCase);
                    for (var i = 0; i < 8; i++) map.Add("Key" + i, i);
                    try { map.Add("Key0", 999); return 9; } catch (ArgumentException) { }
                    if (map.Count != 8 || (int)map["Key0"] != 0) return 10;
                    for (var i = 8; i < 24; i++) map["Key" + i] = i;
                    if (map.Count != 24) return 11;
                    for (var i = 0; i < 24; i++)
                    {
                        if ((int)map["Key" + i] != i) return 12;
                        if (ignoreCase && (int)map["kEy" + i] != i) return 13;
                        if (!ignoreCase && map.Contains("kEy" + i)) return 14;
                    }
                    map["Key0"] = null;
                    if (!map.Contains("Key0") || map["Key0"] != null || map.Count != 24) return 15;
                    var entries = new DictionaryEntry[25]; map.CopyTo(entries, 1);
                    var sum = 0; for (var i = 1; i < entries.Length; i++) if (entries[i].Value != null) sum += (int)entries[i].Value;
                    if (sum != 276) return 16;
                    var seen = 0; foreach (DictionaryEntry entry in map) { if (!map.Contains(entry.Key)) return 17; seen++; }
                    if (seen != 24 || map.Keys.Count != 24 || map.Values.Count != 24) return 18;
                    var e = map.GetEnumerator(); e.MoveNext(); map["Key1"] = 25;
                    try { e.MoveNext(); return 19; } catch (InvalidOperationException) { }
                    for (var i = 0; i < 24; i++) map.Remove((ignoreCase ? "key" : "Key") + i);
                    if (map.Count != 0) return 20;
                    map.Clear(); map.Add("After", 42);
                    if ((int)map[ignoreCase ? "AFTER" : "After"] != 42 || map.Count != 1) return 21;
                    try { map.Add(null, 1); return 22; } catch (ArgumentNullException) { }
                    try { map[null] = 1; return 23; } catch (ArgumentNullException) { }
                }
                var switchOnSet = new HybridDictionary(true);
                for (var i = 0; i < 8; i++) switchOnSet.Add("A" + i, i);
                switchOnSet["a0"] = 42;
                if (switchOnSet.Count != 8 || (int)switchOnSet["A0"] != 42) return 24;
                switchOnSet.Clear(); if (switchOnSet.Count != 0) return 25;
                return input + 1;
            }
        }
        """;
}
