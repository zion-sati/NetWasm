using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConcurrentQueueCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void QueuePreservesFifoSnapshotsAndCollectionContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "ConcurrentQueueFixture", Source, "ConcurrentQueueFixture.EntryPoint", false, target, 0, []));
        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        namespace ConcurrentQueueFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var queue = new ConcurrentQueue<int>();
                if (!queue.IsEmpty || queue.TryDequeue(out var empty) || empty != 0 || queue.TryPeek(out _)) return 1;
                for (var i = 0; i < 80; i++) queue.Enqueue(i);
                for (var i = 0; i < 40; i++) if (!queue.TryDequeue(out var value) || value != i) return 2;
                for (var i = 80; i < 160; i++) queue.Enqueue(i);
                if (queue.Count != 120 || !queue.TryPeek(out var first) || first != 40) return 3;
                var snapshot = queue.GetEnumerator();
                queue.Clear();
                queue.Enqueue(999);
                for (var i = 40; i < 160; i++) if (!snapshot.MoveNext() || snapshot.Current != i) return 4;
                if (snapshot.MoveNext() || queue.Count != 1) return 5;
                try { snapshot.Reset(); return 13; } catch (NotSupportedException) { }
                snapshot.Dispose();
                var words = new ConcurrentQueue<string>(new[] { "one", "two" });
                var view = (IProducerConsumerCollection<string>)words;
                if (!view.TryAdd(null) || !view.TryTake(out var word) || word != "one") return 6;
                var array = view.ToArray();
                words.Clear();
                if (array.Length != 2 || array[0] != "two" || array[1] != null) return 7;
                words.Enqueue("three");
                var copied = new string[3];
                view.CopyTo(copied, 1);
                if (copied[1] != "three" || copied[0] != null) return 8;
                var objects = new object[2];
                ((ICollection)words).CopyTo(objects, 1);
                if ((string)objects[1] != "three" || ((ICollection)words).IsSynchronized) return 9;
                var errors = 0;
                try { _ = new ConcurrentQueue<int>(null); } catch (ArgumentNullException) { errors++; }
                try { words.CopyTo(null, 0); } catch (ArgumentNullException) { errors++; }
                try { words.CopyTo(copied, -1); } catch (ArgumentOutOfRangeException) { errors++; }
                try { words.CopyTo(copied, 4); } catch (ArgumentException) { errors++; }
                try { _ = ((ICollection)words).SyncRoot; } catch (NotSupportedException) { errors++; }
                try { ((ICollection)words).CopyTo(null, 0); } catch (ArgumentNullException) { errors++; }
                if (errors != 6) return 10;
                var enumerator = ((IEnumerable)words).GetEnumerator();
                words.Clear();
                if (!enumerator.MoveNext() || (string)enumerator.Current != "three" || enumerator.MoveNext()) return 11;
                if (!words.IsEmpty || !((IReadOnlyCollection<string>)words).Count.Equals(0)) return 12;
                return 42;
            }
        }
        """;
}
