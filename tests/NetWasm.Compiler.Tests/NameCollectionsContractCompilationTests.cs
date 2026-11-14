using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NameCollectionsContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NameLookupAndMultipleValuesMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "NameFixture", Source, "NameFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace NameFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var n = new NameValueCollection();
                n.Add("A", "one"); n.Add("a", "two"); n.Add(null, "null-key"); n.Add("empty", null);
                if (n.Count != 3 || n["a"] != "one,two" || n[null] != "null-key" || n["empty"] != null) return 1;
                var vals = n.GetValues("A"); if (vals.Length != 2 || vals[0] != "one" || vals[1] != "two") return 2;
                vals[0] = "changed"; if (n.GetValues(0)[0] != "one") return 3;
                if (n.GetKey(0) != "A" || n.AllKeys[1] != null || !n.HasKeys()) return 4;
                var copy = new NameValueCollection(n); copy.Set("A", "replacement"); if (n["A"] != "one,two" || copy[0] != "replacement") return 5;
                var all = n.AllKeys; n.Add("new", "value"); if (n.AllKeys.Length != 4 || all.Length != 3) return 6;
                var values = new string[4]; n.CopyTo(values, 0); if (values[0] != "one,two" || values[1] != "null-key") return 7;
                var e = n.GetEnumerator(); e.MoveNext(); n.Remove("A");
                try { e.MoveNext(); return 8; } catch (InvalidOperationException) { }
                n.Clear(); n.Add(null, null); if (n.HasKeys() || n.Count != 1 || n.GetValues(null) != null) return 9;
                var ordinal = new NameValueCollection(StringComparer.Ordinal); ordinal.Add("a", "1"); ordinal.Add("A", "2"); if (ordinal.Count != 2) return 10;
                var b = new Bag(); b.Add("A", "first"); b.Add("a", "second"); b.Add(null, "nil");
                if (b.Count != 3 || (string)b.Get("a") != "first" || (string)b.Get(1) != "second") return 11;
                var typed = b.AllValues(typeof(string));
                if (typed is not string[] || typed.Length != 3 || (string)typed[1] != "second") return 20;
                var keys = b.Keys; if (keys.Count != 3 || keys[0] != "A" || keys.Get(2) != null) return 12;
                var keyCopy = new string[3]; ((ICollection)keys).CopyTo(keyCopy, 0); if (keyCopy[1] != "a") return 13;
                if (b.AllValues().Length != 3 || b.AllKeys().Length != 3) return 14;
                b.Set("A", "updated"); if ((string)b.Get(0) != "updated") return 15;
                b.Remove("a"); if (b.Count != 1 || b.Get("a") != null || b.HasKeys()) return 16;
                b.ReadOnly = true; try { b.Add("x", 1); return 17; } catch (NotSupportedException) { }
                b.ReadOnly = false; b.Clear(); if (keys.Count != 0) return 18;
                try { new NameValueCollection((NameValueCollection)null); return 19; } catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        public sealed class Bag : NameObjectCollectionBase
        {
            public void Add(string name, object value) => BaseAdd(name, value);
            public object Get(string name) => BaseGet(name);
            public object Get(int index) => BaseGet(index);
            public void Set(string name, object value) => BaseSet(name, value);
            public void Remove(string name) => BaseRemove(name);
            public void Clear() => BaseClear();
            public bool HasKeys() => BaseHasKeys();
            public object[] AllValues() => BaseGetAllValues();
            public object[] AllValues(Type type) => BaseGetAllValues(type);
            public string[] AllKeys() => BaseGetAllKeys();
            public bool ReadOnly { get => IsReadOnly; set => IsReadOnly = value; }
        }
        """;
}
