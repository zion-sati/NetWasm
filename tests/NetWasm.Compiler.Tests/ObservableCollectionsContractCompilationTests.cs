using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ObservableCollectionsContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NotificationOrderAndReentrancyMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ObservableFixture", Source, "ObservableFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Collections.Specialized;
        using System.ComponentModel;
        namespace ObservableFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var items = new ObservableCollection<int>(new int[] { 1, 2 });
                var view = new ReadOnlyObservableCollection<int>(items);
                var log = ""; var viewLog = "";
                ((INotifyPropertyChanged)items).PropertyChanged += (s, e) => log += e.PropertyName + ";";
                items.CollectionChanged += (s, e) => log += e.Action.ToString() + ";";
                ((INotifyPropertyChanged)view).PropertyChanged += (s, e) => viewLog += e.PropertyName + ";";
                ((INotifyCollectionChanged)view).CollectionChanged += (s, e) => viewLog += e.Action.ToString() + ";";
                items.Add(3); items[0] = 4; items.Move(0, 2); items.RemoveAt(1); items.Clear();
                var expected = "Count;Item[];Add;Item[];Replace;Item[];Move;Count;Item[];Remove;Count;Item[];Reset;";
                if (log != expected || viewLog != expected || view.Count != 0) return 1;
                try { ((IList<int>)view).Add(5); return 2; } catch (NotSupportedException) { }
                var list = new List<int>(); list.Add(42); var copied = new ObservableCollection<int>(list); list.Clear();
                if (copied.Count != 1 || copied[0] != 42) return 3;
                var single = new ObservableCollection<int>(); var entered = false;
                single.CollectionChanged += (s, e) => { if (!entered) { entered = true; single.Add(2); } };
                single.Add(1); if (single.Count != 2) return 4;
                var multi = new ObservableCollection<int>(); var rejected = false; var calls = 0;
                multi.CollectionChanged += (s, e) => { try { multi.Add(2); } catch (InvalidOperationException) { rejected = true; } };
                multi.CollectionChanged += (s, e) => calls++;
                multi.Add(1); if (!rejected || calls != 1 || multi.Count != 1) return 5;
                var throwing = new ObservableCollection<int>();
                NotifyCollectionChangedEventHandler failure = (s, e) => throw new InvalidOperationException();
                throwing.CollectionChanged += failure;
                try { throwing.Add(1); return 6; } catch (InvalidOperationException) { }
                throwing.CollectionChanged -= failure; throwing.Add(2); if (throwing.Count != 2) return 7;
                var guarded = new Guarded(); guarded.CollectionChanged += (s, e) => { }; guarded.CollectionChanged += (s, e) => { };
                using (guarded.Guard()) { try { guarded.Add(1); return 8; } catch (InvalidOperationException) { } }
                guarded.Add(2); if (guarded.Count != 1) return 9;
                try { new ObservableCollection<int>((IEnumerable<int>)null); return 10; } catch (ArgumentNullException) { }
                try { new ObservableCollection<int>((List<int>)null); return 11; } catch (ArgumentNullException) { }
                try { new ReadOnlyObservableCollection<int>(null); return 12; } catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        public sealed class Guarded : ObservableCollection<int>
        {
            public IDisposable Guard() => BlockReentrancy();
        }
        """;
}
