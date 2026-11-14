using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BindingListCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PrimitiveBindingListsPreserveNotificationsAndCancellation(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CategoryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CategoryFixture.EntryPoint",
            "Run", [new("run", "CategoryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        namespace CategoryFixture;
        public sealed class Item { }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var list = new BindingList<int>(); IBindingList binding = list; ICancelAddNew cancel = list;
                var calls = 0; var kind = ListChangedType.Reset; var index = -1;
                ListChangedEventHandler handler = (sender, args) => { calls++; kind = args.ListChangedType; index = args.NewIndex; };
                list.ListChanged += handler;
                if (!list.AllowNew || !list.AllowEdit || !list.AllowRemove || !binding.SupportsChangeNotification || binding.SupportsSearching || binding.SupportsSorting || ((IRaiseItemChangedEvents)list).RaisesItemChangedEvents) return 1;
                list.Add(4); if (kind != ListChangedType.ItemAdded || index != 0 || calls != 1) return 2;
                list[0] = 5; if (kind != ListChangedType.ItemChanged || index != 0) return 3;
                list.RemoveAt(0); if (kind != ListChangedType.ItemDeleted || index != 0 || list.Count != 0) return 4;
                if (list.AddNew() != 0 || list.Count != 1) return 5;
                cancel.CancelNew(0); if (list.Count != 0) return 6;
                AddingNewEventHandler adding = (sender, args) => args.NewObject = 99;
                list.AddingNew += adding; if (list.AddNew() != 99 || list[0] != 99) return 7;
                cancel.EndNew(0); cancel.CancelNew(0); if (list.Count != 1) return 8;
                list.AddingNew -= adding;
                list.AllowEdit = false; if (kind != ListChangedType.Reset || binding.AllowEdit) return 9;
                list.AllowNew = false; if (binding.AllowNew) return 10;
                list.AllowRemove = false;
                var failed = 0;
                try { list.RemoveAt(0); } catch (NotSupportedException) { failed++; }
                try { binding.ApplySort(null, ListSortDirection.Ascending); } catch (NotSupportedException) { failed++; }
                try { binding.Find(null, 99); } catch (NotSupportedException) { failed++; }
                try { binding.RemoveSort(); } catch (NotSupportedException) { failed++; }
                list.AllowRemove = true; var before = calls; list.RaiseListChangedEvents = false; list.Add(7); if (calls != before) return 11;
                list.RaiseListChangedEvents = true; list.ResetItem(0); if (kind != ListChangedType.ItemChanged || index != 0) return 12;
                list.ResetBindings(); if (kind != ListChangedType.Reset || index != -1) return 13;
                list.ListChanged -= handler; before = calls; list.Clear(); if (calls != before) return 14;
                var storage = new List<int>(); var supplied = new BindingList<int>(storage); supplied.Add(42); if (storage.Count != 1 || storage[0] != 42) return 15;
                try { new BindingList<Item>(); } catch (PlatformNotSupportedException e) { if (!string.IsNullOrEmpty(e.Message)) failed++; }
                try { new BindingList<string>(); } catch (PlatformNotSupportedException) { failed++; }
                return failed == 6 ? input + 1 : 100 + failed;
            }
        }
        """;
}
