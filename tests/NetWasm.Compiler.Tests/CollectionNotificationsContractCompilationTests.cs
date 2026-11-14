using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CollectionNotificationsContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EventArgumentsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "NotificationFixture", Source, "NotificationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        namespace NotificationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var reset = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                if (reset.NewItems != null || reset.OldItems != null || reset.NewStartingIndex != -1 || reset.OldStartingIndex != -1) return 1;
                var add = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (object)42, 3);
                if (add.Action != NotifyCollectionChangedAction.Add || add.NewStartingIndex != 3 || (int)add.NewItems[0] != 42 || add.OldItems != null) return 2;
                var one = add.NewItems;
                if (!one.IsReadOnly || !one.IsFixedSize || !one.Contains(42) || one.IndexOf(41) != -1 || one.Count != 1) return 3;
                var copy = new int[2]; one.CopyTo(copy, 1); if (copy[1] != 42) return 4;
                var e = one.GetEnumerator(); if (!e.MoveNext() || (int)e.Current != 42 || e.MoveNext()) return 5;
                try { one.Add(2); return 6; } catch (NotSupportedException) { }
                try { one[0] = 2; return 7; } catch (NotSupportedException) { }
                try { var x = one[1]; return 8; } catch (ArgumentOutOfRangeException) { }
                var list = new ArrayList(); list.Add("a");
                var remove = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, list, 2);
                if (remove.OldStartingIndex != 2 || remove.NewItems != null || (string)remove.OldItems[0] != "a") return 9;
                list.Add("b"); if (remove.OldItems.Count != 2 || !remove.OldItems.Contains("b")) return 10;
                try { remove.OldItems.Clear(); return 11; } catch (NotSupportedException) { }
                var replace = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, (object)2, (object)1, 4);
                if (replace.NewStartingIndex != 4 || replace.OldStartingIndex != 4 || (int)replace.NewItems[0] != 2 || (int)replace.OldItems[0] != 1) return 12;
                var move = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, (object)null, 2, -1);
                if (!ReferenceEquals(move.NewItems, move.OldItems) || move.NewItems[0] != null || move.OldStartingIndex != -1) return 13;
                var rangeMove = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, (IList)null, 0, 1);
                if (rangeMove.NewItems != null || rangeMove.OldItems != null) return 14;
                var rangeReplace = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, list, new ArrayList(), -2);
                if (rangeReplace.NewStartingIndex != -2 || rangeReplace.NewItems.Count != 2 || rangeReplace.OldItems.Count != 0) return 15;
                var single = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (object)1, -2);
                if (single.NewStartingIndex != -2) return 16;
                try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, list, -2); return 17; } catch (ArgumentOutOfRangeException) { }
                try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add); return 18; } catch (ArgumentException) { }
                try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, (object)1); return 19; } catch (ArgumentException) { }
                try { new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)null); return 20; } catch (ArgumentNullException) { }
                var publisher = new Publisher(); INotifyCollectionChanged source = publisher;
                var seen = 0; NotifyCollectionChangedEventHandler handler = (sender, args) => { if (ReferenceEquals(sender, publisher) && ReferenceEquals(args, add)) seen++; };
                source.CollectionChanged += handler; publisher.Raise(add); source.CollectionChanged -= handler; publisher.Raise(add);
                return seen == 1 ? input + 1 : 21;
            }
        }
        public sealed class Publisher : INotifyCollectionChanged
        {
            public event NotifyCollectionChangedEventHandler CollectionChanged;
            public void Raise(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
        }
        """;
}
