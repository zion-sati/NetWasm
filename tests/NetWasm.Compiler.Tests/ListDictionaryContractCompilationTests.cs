using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ListDictionaryContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EqualityViewsCopyAndMutationMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "DictionaryFixture", Source, "DictionaryFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace DictionaryFixture;
        public sealed class Key
        {
            public int Id;
            public Key(int id) { Id = id; }
            public override bool Equals(object other) => other is Key key && key.Id == Id;
            public override int GetHashCode() => Id;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var map = new ListDictionary();
                if (map.Count != 0 || map.IsReadOnly || map.IsFixedSize || map.IsSynchronized || !ReferenceEquals(map.SyncRoot, map)) return 1;
                map.Add("a", 1); map.Add("b", null); map["c"] = 3;
                if (map.Count != 3 || !map.Contains("b") || map["b"] != null || map["missing"] != null) return 2;
                var keys = map.Keys; var values = map.Values;
                if (!ReferenceEquals(keys.SyncRoot, map.SyncRoot) || !ReferenceEquals(values.SyncRoot, map.SyncRoot)) return 3;
                var keyCopy = new string[3]; keys.CopyTo(keyCopy, 0);
                if (keyCopy[0] != "a" || keyCopy[1] != "b" || keyCopy[2] != "c") return 4;
                var valueCopy = new int[3]; values.CopyTo(valueCopy, 0);
                if (valueCopy[0] != 1 || valueCopy[1] != 0 || valueCopy[2] != 3) return 5;
                var entries = new DictionaryEntry[4]; map.CopyTo(entries, 1);
                if ((string)entries[1].Key != "a" || (int)entries[3].Value != 3) return 6;
                var e = map.GetEnumerator();
                try { var current = e.Entry; return 7; } catch (InvalidOperationException) { }
                if (!e.MoveNext() || (string)e.Key != "a" || (int)e.Value != 1) return 8;
                e.Reset(); if (!e.MoveNext() || (string)e.Key != "a") return 9;
                map["a"] = 10;
                try { e.MoveNext(); return 10; } catch (InvalidOperationException) { }
                var keysEnumerator = keys.GetEnumerator(); keysEnumerator.MoveNext();
                try { map.Add("a", 20); return 11; } catch (ArgumentException) { }
                if ((int)map["a"] != 10 || map.Count != 3) return 12;
                try { keysEnumerator.Reset(); return 13; } catch (InvalidOperationException) { }
                var absentEnumerator = map.GetEnumerator(); absentEnumerator.MoveNext(); map.Remove("absent");
                try { absentEnumerator.MoveNext(); return 14; } catch (InvalidOperationException) { }
                map.Remove("b"); map.Remove("a"); map.Remove("c");
                if (map.Count != 0 || keys.Count != 0 || values.Count != 0) return 15;
                map.Add("last", 4); if (keys.Count != 1 || values.Count != 1) return 16;
                var last = map.GetEnumerator(); last.MoveNext();
                if (last.MoveNext()) return 17;
                try { var current = last.Current; return 18; } catch (InvalidOperationException) { }
                try { map.Add(null, 1); return 19; } catch (ArgumentNullException x) { if (x.ParamName != "key") return 20; }
                try { map.Contains(null); return 21; } catch (ArgumentNullException) { }
                try { map.Remove(null); return 22; } catch (ArgumentNullException) { }
                try { var item = map[null]; return 23; } catch (ArgumentNullException) { }
                try { map[null] = 1; return 24; } catch (ArgumentNullException) { }
                try { map.CopyTo(new DictionaryEntry[0], 0); return 25; } catch (ArgumentException) { }
                try { map.CopyTo(null, 0); return 26; } catch (ArgumentNullException) { }
                map.Clear(); if (map.Count != 0 || keys.Count != 0) return 27;
                map.Add(new Key(7), 42);
                if ((int)map[new Key(7)] != 42) return 28;
                var insensitive = new ListDictionary(StringComparer.OrdinalIgnoreCase);
                insensitive.Add("Key", 1); insensitive["KEY"] = 2;
                if (insensitive.Count != 1 || (int)insensitive["key"] != 2) return 29;
                try { insensitive.Add("key", 3); return 30; } catch (ArgumentException) { }
                insensitive.Remove("kEy"); if (insensitive.Count != 0) return 31;
                return input + 1;
            }
        }
        """;
}
