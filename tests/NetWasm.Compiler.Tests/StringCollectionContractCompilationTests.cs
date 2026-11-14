using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StringCollectionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypedAndUntypedStringCollectionContractsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "StringFixture", Source, "StringFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace StringFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var strings = new StringCollection();
                var list = (IList)strings;
                if (strings.Count != 0 || strings.IsReadOnly || strings.IsSynchronized || list.IsFixedSize) return 1;
                if (!ReferenceEquals(strings.SyncRoot, ((ICollection)list).SyncRoot)) return 2;
                if (strings.Add("A") != 0 || list.Add(null) != 1) return 3;
                strings.AddRange(new[] { "b", "A" });
                strings.Insert(1, "middle");
                if (strings.Count != 5 || strings[1] != "middle" || strings[2] != null) return 4;
                if (!strings.Contains(null) || strings.Contains("a") || strings.IndexOf("A") != 0 || list.IndexOf(null) != 2) return 5;
                list[1] = "changed"; if (strings[1] != "changed") return 6;
                var copy = new string[7]; strings.CopyTo(copy, 1);
                if (copy[1] != "A" || copy[2] != "changed" || copy[3] != null || copy[5] != "A") return 7;
                var objects = new object[5]; ((ICollection)strings).CopyTo(objects, 0);
                if ((string)objects[3] != "b") return 8;
                try { list.Add(123); return 9; } catch (InvalidCastException) { }
                try { list.Contains(123); return 10; } catch (InvalidCastException) { }
                try { list.IndexOf(123); return 11; } catch (InvalidCastException) { }
                try { list.Insert(0, 123); return 12; } catch (InvalidCastException) { }
                try { list.Remove(123); return 13; } catch (InvalidCastException) { }
                try { list[0] = 123; return 14; } catch (InvalidCastException) { }
                if (strings.Count != 5 || strings[0] != "A") return 15;
                try { strings.AddRange(null); return 16; } catch (ArgumentNullException e) { if (e.ParamName != "value") return 17; }
                var e1 = strings.GetEnumerator();
                try { var current = e1.Current; return 18; } catch (InvalidOperationException) { }
                if (!e1.MoveNext() || e1.Current != "A") return 19;
                e1.Reset(); if (!e1.MoveNext() || e1.Current != "A") return 20;
                var count = 1; while (e1.MoveNext()) count++;
                if (count != 5) return 21;
                try { var current = e1.Current; return 22; } catch (InvalidOperationException) { }
                var e2 = strings.GetEnumerator(); e2.MoveNext(); strings[0] = "updated";
                try { e2.MoveNext(); return 23; } catch (InvalidOperationException) { }
                try { e2.Reset(); return 24; } catch (InvalidOperationException) { }
                strings.Remove("A"); if (strings.Count != 4) return 25;
                strings.Remove("absent"); if (strings.Count != 4) return 26;
                strings.RemoveAt(1); list.Remove(null);
                if (strings.Count != 2 || strings[0] != "updated" || strings[1] != "b") return 27;
                var plain = ((IEnumerable)strings).GetEnumerator();
                if (!plain.MoveNext() || (string)plain.Current != "updated") return 28;
                strings.Clear(); if (strings.Count != 0 || strings.GetEnumerator().MoveNext()) return 29;
                try { strings.RemoveAt(0); return 30; } catch (ArgumentOutOfRangeException) { }
                return input + 1;
            }
        }
        """;
}
