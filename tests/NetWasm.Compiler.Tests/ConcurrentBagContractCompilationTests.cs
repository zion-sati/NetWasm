using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConcurrentBagContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BagOperationsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ConcurrentBagFixture", Source, "ConcurrentBagFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        namespace ConcurrentBagFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var empty = new ConcurrentBag<int>();
                if (!empty.IsEmpty || empty.Count != 0 || empty.TryPeek(out var value) || value != 0 || empty.TryTake(out value) || value != 0) return 1;
                empty.CopyTo(Array.Empty<int>(), 99);
                empty.Clear(); empty.CopyTo(Array.Empty<int>(), 99);
                try { new ConcurrentBag<int>(Array.Empty<int>()).CopyTo(Array.Empty<int>(), 1); return 2; } catch (ArgumentException ex) { if (ex.ParamName != "index") return 3; }
                empty.Add(42); empty.Clear();
                try { empty.CopyTo(Array.Empty<int>(), 1); return 4; } catch (ArgumentException ex) { if (ex.ParamName != "index") return 5; }
                var bag = new ConcurrentBag<int>(new[] { 1, 2, 2 });
                if (bag.Count != 3 || bag.IsEmpty || !bag.TryPeek(out value) || value != 2) return 6;
                var snapshot = bag.GetEnumerator(); bag.Add(42); bag.Clear();
                if (!snapshot.MoveNext() || snapshot.Current != 2 || !snapshot.MoveNext() || snapshot.Current != 2 || !snapshot.MoveNext() || snapshot.Current != 1 || snapshot.MoveNext()) return 7;
                snapshot.Dispose();
                IProducerConsumerCollection<int> producer = bag;
                if (!producer.TryAdd(20) || !producer.TryAdd(22) || !producer.TryTake(out value) || value != 22) return 8;
                if (!producer.TryTake(out value) || value != 20 || producer.TryTake(out value)) return 9;
                bag.Add(20); bag.Add(22);
                var copy = new int[3]; bag.CopyTo(copy, 1);
                if (copy[1] != 22 || copy[2] != 20) return 10;
                var boxed = new object[2]; ((ICollection)bag).CopyTo(boxed, 0);
                if (!Equals(boxed[0], 22) || !Equals(boxed[1], 20) || ((ICollection)bag).IsSynchronized) return 11;
                var array = bag.ToArray(); array[0] = 999;
                if (!bag.TryPeek(out value) || value != 22) return 12;
                var untyped = ((IEnumerable)bag).GetEnumerator(); bag.Clear();
                if (!untyped.MoveNext() || !Equals(untyped.Current, 22) || !untyped.MoveNext() || !Equals(untyped.Current, 20) || untyped.MoveNext()) return 13;
                if (((IReadOnlyCollection<int>)bag).Count != 0) return 14;
                var nullable = new ConcurrentBag<string?>(); nullable.Add(null);
                if (!nullable.TryTake(out var text) || text != null) return 15;
                var objects = new ConcurrentBag<object>(); objects.Add(new object());
                try { objects.CopyTo(new string[1], 0); return 16; } catch (InvalidCastException ex) { if (ex.InnerException is not ArrayTypeMismatchException) return 17; }
                try { _ = ((ICollection)bag).SyncRoot; return 18; } catch (NotSupportedException) { }
                try { new ConcurrentBag<int>(null!); return 19; } catch (ArgumentNullException ex) { if (ex.ParamName != "collection") return 20; }
                try { bag.CopyTo(null!, 0); return 21; } catch (ArgumentNullException ex) { if (ex.ParamName != "array") return 22; }
                try { bag.CopyTo(Array.Empty<int>(), -1); return 23; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "index") return 24; }
                try { ((ICollection)bag).CopyTo(null!, 0); return 25; } catch (ArgumentNullException ex) { if (ex.ParamName != "array") return 26; }
                return input + 1;
            }
        }
        """;
}
