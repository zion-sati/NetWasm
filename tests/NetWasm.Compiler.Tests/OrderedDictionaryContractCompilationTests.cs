using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class OrderedDictionaryContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OrderedLookupAndViewsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "OrderedFixture", Source, "OrderedFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace OrderedFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var map = new OrderedDictionary(4, StringComparer.OrdinalIgnoreCase);
                IOrderedDictionary ordered = map;
                map.Add("A", 1); map.Add("C", 3); ordered.Insert(1, "B", 2);
                var keys = map.Keys; var values = map.Values;
                if ((int)ordered[1] != 2 || (int)map[(object)"b"] != 2 || !map.Contains("a")) return 1;
                var enumerator = ordered.GetEnumerator(); var text = "";
                while (enumerator.MoveNext()) text += enumerator.Key;
                if (text != "ABC") return 2;
                ordered[1] = 20; map[(object)"a"] = 10;
                if ((int)map[(object)"B"] != 20 || (int)map[0] != 10) return 3;
                try { map.Add("b", 9); return 4; } catch (ArgumentException) { }
                if (map.Count != 3) return 5;
                var view = map.AsReadOnly(); map.Add("D", null);
                if (view.Count != 4 || !view.IsReadOnly || !((IDictionary)view).IsFixedSize || keys.Count != 4 || values.Count != 4) return 6;
                try { view.Add("E", 5); return 7; } catch (NotSupportedException) { }
                try { view[0] = 7; return 8; } catch (NotSupportedException) { }
                var keyCopy = new object[4]; keys.CopyTo(keyCopy, 0);
                if ((string)keyCopy[0] != "a" || (string)keyCopy[1] != "B" || (string)keyCopy[3] != "D") return 9;
                var entries = new DictionaryEntry[4]; map.CopyTo(entries, 0);
                var sum = 0; foreach (var entry in entries) if (entry.Value != null) sum += (int)entry.Value;
                if (sum != 33) return 10;
                enumerator = map.GetEnumerator(); enumerator.MoveNext(); map[0] = 11;
                try { enumerator.MoveNext(); return 11; } catch (InvalidOperationException) { }
                map.Remove("B"); map.RemoveAt(0); map.Remove("absent");
                if (map.Count != 2 || (int)map[0] != 3) return 12;
                try { map.Insert(-1, "x", 1); return 13; } catch (ArgumentOutOfRangeException) { }
                try { map.RemoveAt(2); return 14; } catch (ArgumentOutOfRangeException) { }
                try { map.Contains(null); return 15; } catch (ArgumentNullException) { }
                map.Clear(); if (view.Count != 0 || keys.Count != 0 || values.Count != 0) return 16;
                map.Add(42, "answer"); if ((string)map[(object)42] != "answer" || (string)map[0] != "answer") return 17;
                return input + 1;
            }
        }
        """;
}
