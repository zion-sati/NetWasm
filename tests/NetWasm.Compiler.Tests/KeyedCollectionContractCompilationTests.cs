using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class KeyedCollectionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void KeyChangesAndLookupModesMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "KeyedFixture", Source, "KeyedFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        namespace KeyedFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                foreach (var threshold in new int[] { -1, 0, 2 })
                {
                    var items = new Items(threshold);
                    var a = new Item("A", 1); var b = new Item("B", 2);
                    items.Add(a); items.Add(new Item(null, 0)); items.Add(b);
                    if (items.HasDictionary != (threshold == 0)) return 1;
                    items.Add(new Item("C", 3));
                    if (items.HasDictionary != (threshold != -1) || items["a"] != a || !items.Contains("b")) return 2;
                    if (!items.TryGetValue("c", out var found) || found.Value != 3 || items.TryGetValue("absent", out found) || found != null) return 3;
                    try { items.Add(new Item("a", 9)); return 4; } catch (ArgumentException) { }
                    if (items.Count != 4 || items["A"] != a) return 5;
                    items.Rename(a, "D"); if (items.Contains("a") || items["d"] != a) return 6;
                    try { items.Rename(a, "b"); return 7; } catch (ArgumentException) { }
                    if (items["d"] != a || items["b"] != b) return 8;
                    items.Rename(a, null); if (items.Contains("d")) return 9;
                    items.Rename(a, "E"); if (items["e"] != a) return 10;
                    items[0] = new Item("e", 8); if (items["E"].Value != 8) return 11;
                    try { items[0] = new Item("B", 10); return 12; } catch (ArgumentException) { }
                    if (items[0].Value != 8 || items["b"] != b) return 13;
                    if (!items.Remove("B") || items.Remove("missing") || items.Contains("b")) return 14;
                    try { items.Contains((string)null); return 15; } catch (ArgumentNullException) { }
                    try { var absent = items["missing"]; return 16; } catch (KeyNotFoundException) { }
                    try { items.Rename(new Item("outside", 0), "x"); return 17; } catch (ArgumentException) { }
                    items.Clear(); if (items.Count != 0 || items.Contains("E")) return 18;
                    items.Add(new Item("again", 42)); if (items["AGAIN"].Value != 42) return 19;
                }
                try { new Items(-2); return 20; } catch (ArgumentOutOfRangeException) { }
                return input + 1;
            }
        }
        public sealed class Item
        {
            public string Key; public int Value;
            public Item(string key, int value) { Key = key; Value = value; }
        }
        public sealed class Items : KeyedCollection<string, Item>
        {
            public Items(int threshold) : base(StringComparer.OrdinalIgnoreCase, threshold) { }
            protected override string GetKeyForItem(Item item) => item.Key;
            public bool HasDictionary => Dictionary != null;
            public void Rename(Item item, string key) { ChangeItemKey(item, key); item.Key = key; }
        }
        """;
}
