using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CollectionBaseContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void HooksRollbackAndReadOnlyViewsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "BaseFixture", Source, "BaseFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        namespace BaseFixture;
        public sealed class ListProbe : CollectionBase
        {
            public string Log = "";
            public string Fail = "";
            public IList View => List;
            public ListProbe() : base(4) { }
            private void Mark(string name) { Log += name; if (Fail == name) throw new InvalidOperationException(); }
            protected override void OnValidate(object value) { base.OnValidate(value); Mark("V"); }
            protected override void OnInsert(int index, object value) => Mark("I");
            protected override void OnInsertComplete(int index, object value) => Mark("i");
            protected override void OnSet(int index, object oldValue, object newValue) => Mark("S");
            protected override void OnSetComplete(int index, object oldValue, object newValue) => Mark("s");
            protected override void OnRemove(int index, object value) => Mark("R");
            protected override void OnRemoveComplete(int index, object value) => Mark("r");
            protected override void OnClear() => Mark("C");
            protected override void OnClearComplete() => Mark("c");
        }
        public sealed class MapProbe : DictionaryBase
        {
            public string Log = "";
            public string Fail = "";
            public IDictionary View => Dictionary;
            private void Mark(string name) { Log += name; if (Fail == name) throw new InvalidOperationException(); }
            protected override void OnValidate(object key, object value) => Mark("V");
            protected override void OnInsert(object key, object value) => Mark("I");
            protected override void OnInsertComplete(object key, object value) => Mark("i");
            protected override void OnSet(object key, object oldValue, object newValue) => Mark("S");
            protected override void OnSetComplete(object key, object oldValue, object newValue) => Mark("s");
            protected override void OnRemove(object key, object value) => Mark("R");
            protected override void OnRemoveComplete(object key, object value) => Mark("r");
            protected override void OnClear() => Mark("C");
            protected override void OnClearComplete() => Mark("c");
            protected override object OnGet(object key, object value) { Mark("G"); return 999; }
        }
        public sealed class ReadProbe : ReadOnlyCollectionBase
        {
            public void Seed(int value) => InnerList.Add(value);
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var list = new ListProbe(); var view = list.View;
                if (!ReferenceEquals(list, view) || list.Capacity < 4 || view.IsReadOnly || view.IsFixedSize) return 1;
                if (view.Add(10) != 0 || list.Log != "VIi") return 2;
                list.Log = ""; list.Fail = "i";
                try { view.Insert(0, 20); return 3; } catch (InvalidOperationException) { }
                if (list.Count != 1 || (int)view[0] != 10 || list.Log != "VIi") return 4;
                list.Log = ""; list.Fail = "s";
                try { view[0] = 30; return 5; } catch (InvalidOperationException) { }
                if ((int)view[0] != 10 || list.Log != "VSs") return 6;
                list.Log = ""; list.Fail = "r";
                try { list.RemoveAt(0); return 7; } catch (InvalidOperationException) { }
                if (list.Count != 1 || (int)view[0] != 10 || list.Log != "VRr") return 8;
                list.Fail = ""; list.Log = "";
                try { view.Add(null); return 9; } catch (ArgumentNullException) { }
                if (list.Count != 1 || list.Log != "") return 10;
                try { view.Remove(99); return 11; } catch (ArgumentException) { }
                view.Insert(1, 20); var copied = new int[3]; ((ICollection)list).CopyTo(copied, 1);
                if (copied[1] != 10 || copied[2] != 20 || view.IndexOf(20) != 1 || !view.Contains(10)) return 12;
                var e = list.GetEnumerator(); e.MoveNext(); view.Add(30);
                try { e.MoveNext(); return 13; } catch (InvalidOperationException) { }
                list.Fail = "c";
                try { list.Clear(); return 14; } catch (InvalidOperationException) { }
                if (list.Count != 0) return 15;
                var map = new MapProbe(); var dictionary = map.View;
                if (!ReferenceEquals(map, dictionary) || map.Count != 0) return 16;
                dictionary.Add("key", 10);
                if (map.Log != "VIi" || (int)dictionary["key"] != 10 || map.Log != "VIiG") return 17;
                map.Fail = "s";
                try { dictionary["key"] = 30; return 18; } catch (InvalidOperationException) { }
                if ((int)dictionary["key"] != 10) return 19;
                try { dictionary["new"] = 40; return 20; } catch (InvalidOperationException) { }
                if (dictionary.Contains("new")) return 21;
                map.Fail = ""; dictionary.Add("null", null); map.Fail = "s";
                try { dictionary["null"] = 50; return 22; } catch (InvalidOperationException) { }
                if (!dictionary.Contains("null") || dictionary["null"] != null) return 23;
                map.Fail = "i";
                try { dictionary.Add("failed", 1); return 24; } catch (InvalidOperationException) { }
                if (dictionary.Contains("failed")) return 25;
                map.Fail = "r";
                try { dictionary.Remove("key"); return 26; } catch (InvalidOperationException) { }
                if ((int)dictionary["key"] != 10) return 27;
                map.Log = ""; dictionary.Remove("absent"); if (map.Log != "") return 28;
                var keys = dictionary.Keys; map.Fail = ""; dictionary.Add("last", 60);
                if (keys.Count != 3 || dictionary.Values.Count != 3) return 29;
                var entries = new DictionaryEntry[3]; map.CopyTo(entries, 0);
                var count = 0; foreach (DictionaryEntry item in map) { if (!dictionary.Contains(item.Key)) return 30; count++; }
                if (count != 3) return 31;
                map.Fail = "c";
                try { map.Clear(); return 32; } catch (InvalidOperationException) { }
                if (map.Count != 0) return 33;
                var read = new ReadProbe(); read.Seed(7); read.Seed(8);
                var collection = (ICollection)read;
                if (read.Count != 2 || collection.IsSynchronized || !ReferenceEquals(collection.SyncRoot, collection.SyncRoot)) return 34;
                var readCopy = new int[2]; collection.CopyTo(readCopy, 0);
                if (readCopy[0] != 7 || readCopy[1] != 8) return 35;
                var re = read.GetEnumerator(); re.MoveNext(); read.Seed(9);
                try { re.MoveNext(); return 36; } catch (InvalidOperationException) { }
                return input + 1;
            }
        }
        """;
}
