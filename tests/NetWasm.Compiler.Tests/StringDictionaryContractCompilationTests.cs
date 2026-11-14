using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StringDictionaryContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DictionaryContractsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "StringDictionaryFixture", Source, "StringDictionaryFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace StringDictionaryFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var map = new StringDictionary();
                var keys = map.Keys; var values = map.Values;
                if (map.Count != 0 || map.IsSynchronized || map["missing"] != null) return 1;
                if (!ReferenceEquals(map.SyncRoot, map.SyncRoot)) return 2;
                map.Add("HELLO", "Value"); map.Add("Null", null);
                if (map["hello"] != "Value" || !map.ContainsKey("HeLLo")) return 3;
                if (!map.ContainsValue(null) || !map.ContainsValue("Value") || map.ContainsValue("value")) return 4;
                if (keys.Count != 2 || values.Count != 2) return 5;
                try { map.Add("hello", "duplicate"); return 6; } catch (ArgumentException) { }
                map["Hello"] = "updated";
                var entries = new DictionaryEntry[3]; map.CopyTo(entries, 1);
                var seen = 0;
                foreach (DictionaryEntry entry in map)
                {
                    if ((string)entry.Key != "hello" && (string)entry.Key != "null") return 7;
                    seen++;
                }
                if (seen != 2 || map.Count != 2 || map["HELLO"] != "updated") return 8;
                var copied = 0;
                for (var i = 1; i < 3; i++) if (map.ContainsKey((string)entries[i].Key)) copied++;
                if (copied != 2) return 9;
                var enumerator = map.GetEnumerator(); enumerator.MoveNext(); map.Add("NEXT", "x");
                try { enumerator.MoveNext(); return 10; } catch (InvalidOperationException) { }
                map.Remove("HELLO"); map.Remove("absent");
                if (map.ContainsKey("hello") || map.Count != 2) return 11;
                try { map.Add(null, null); return 12; } catch (ArgumentNullException) { }
                try { map[null] = "x"; return 13; } catch (ArgumentNullException) { }
                try { var value = map[null]; return 14; } catch (ArgumentNullException) { }
                try { map.ContainsKey(null); return 15; } catch (ArgumentNullException) { }
                try { map.Remove(null); return 16; } catch (ArgumentNullException) { }
                map.Clear();
                if (keys.Count != 0 || values.Count != 0 || map.GetEnumerator().MoveNext()) return 17;
                map["REUSE"] = "ok";
                return map["reuse"] == "ok" ? input + 1 : 18;
            }
        }
        """;
}
