using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BlockingCollectionInstanceCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ImmediateOperationsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "BlockingFixture", Source, "BlockingFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.Concurrent;
        using System.Threading;
        namespace BlockingFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var full = new BlockingCollection<int>(1); full.Add(1);
                var empty = new BlockingCollection<int>(); var caught = 0;
                try { full.Add(2); return 1; } catch (PlatformNotSupportedException) { caught++; }
                try { full.Add(2, CancellationToken.None); return 2; } catch (PlatformNotSupportedException) { caught++; }
                try { full.TryAdd(2, 1); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { full.TryAdd(2, TimeSpan.FromMilliseconds(1)); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Take(); return 5; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Take(CancellationToken.None); return 6; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.TryTake(out _, 1); return 7; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.TryTake(out _, TimeSpan.FromMilliseconds(1)); return 8; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.GetConsumingEnumerable().GetEnumerator().MoveNext(); return 9; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 9 && full.Count == 1 && empty.Count == 0 ? input + 1 : 10;
            }
            public static int Portable(int input)
            {
                var bounded = new BlockingCollection<int>(2);
                if (bounded.BoundedCapacity != 2 || bounded.IsCompleted || bounded.IsAddingCompleted) return 11;
                bounded.Add(20); bounded.Add(22, CancellationToken.None);
                if (bounded.TryAdd(1) || bounded.TryAdd(1, 0) || bounded.TryAdd(1, TimeSpan.Zero)) return 12;
                var copy = new int[2]; bounded.CopyTo(copy, 0);
                if (copy[0] != 20 || copy[1] != 22 || bounded.ToArray().Length != 2) return 13;
                var snapshot = ((IEnumerable<int>)bounded).GetEnumerator();
                if (!bounded.TryTake(out var value, 0) || value != 20 || bounded.Take() != 22 || bounded.TryTake(out value) || value != 0) return 14;
                if (!snapshot.MoveNext() || snapshot.Current != 20 || !snapshot.MoveNext() || snapshot.Current != 22 || snapshot.MoveNext()) return 15;
                if (!bounded.TryAdd(42, 1, CancellationToken.None) || !bounded.TryTake(out value, TimeSpan.FromMilliseconds(1)) || value != 42) return 16;
                bounded.Add(20); bounded.Add(22); bounded.CompleteAdding(); bounded.CompleteAdding();
                if (!bounded.IsAddingCompleted || bounded.IsCompleted) return 17;
                var sum = 0; foreach (var item in bounded.GetConsumingEnumerable()) sum += item;
                if (sum != 42 || !bounded.IsCompleted || bounded.TryTake(out value, -1)) return 18;
                try { bounded.Take(); return 19; } catch (InvalidOperationException) { }
                try { bounded.TryAdd(42); return 20; } catch (InvalidOperationException) { }
                var canceled = new CancellationToken(true);
                try { bounded.TryTake(out value, 0, canceled); return 21; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 22; }
                try { bounded.TryAdd(42, 0, canceled); return 23; } catch (OperationCanceledException) { }
                if (bounded.GetConsumingEnumerable(canceled).GetEnumerator().MoveNext()) return 24;
                var storage = new Storage(); var custom = new BlockingCollection<int>(storage, 1);
                storage.RejectAdd = true;
                try { custom.TryAdd(42); return 25; } catch (InvalidOperationException) { }
                if (custom.Count != 0) return 26;
                storage.RejectAdd = false; if (!custom.TryAdd(42)) return 27;
                storage.RejectTake = true;
                try { custom.TryTake(out value); return 28; } catch (InvalidOperationException) { }
                if (custom.Count != 1) return 29;
                storage.RejectTake = false; if (custom.Take() != 42 || custom.Count != 0) return 30;
                custom.Dispose(); if (storage.Disposals != 0) return 31;
                try { _ = custom.Count; return 32; } catch (ObjectDisposedException) { }
                try { custom.TryAdd(0, -2); return 33; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 34; }
                try { bounded.TryTake(out value, TimeSpan.FromMilliseconds(-2)); return 35; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 36; }
                try { new BlockingCollection<int>((IProducerConsumerCollection<int>)null!, 0); return 37; } catch (ArgumentNullException ex) { if (ex.ParamName != "collection") return 38; }
                try { new BlockingCollection<int>(0); return 39; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "boundedCapacity") return 40; }
                try { new BlockingCollection<int>(new ConcurrentQueue<int>(new[] { 1, 2 }), 1); return 43; } catch (ArgumentException) { }
                var unbounded = new BlockingCollection<int>(new ConcurrentStack<int>(new[] { 20, 22 }));
                if (unbounded.BoundedCapacity != -1 || unbounded.Take() != 22 || unbounded.Take() != 20) return 44;
                var derived = new Derived(); ((IDisposable)derived).Dispose(); if (derived.Disposals != 1) return 45;
                var finalizing = new Derived(); finalizing.Release(false);
                try { _ = finalizing.Count; return 46; } catch (ObjectDisposedException) { }
                if (((ICollection)bounded).IsSynchronized) return 47;
                try { _ = ((ICollection)bounded).SyncRoot; return 48; } catch (NotSupportedException) { }
                return input + 1;
            }
        }
        public sealed class Derived : BlockingCollection<int>
        {
            public int Disposals;
            protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
            public void Release(bool disposing) => Dispose(disposing);
        }
        public sealed class Storage : IProducerConsumerCollection<int>, IDisposable
        {
            private readonly ConcurrentQueue<int> _queue = new ConcurrentQueue<int>();
            public bool RejectAdd, RejectTake;
            public int Disposals;
            public int Count => _queue.Count;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public bool TryAdd(int item) { if (RejectAdd) return false; _queue.Enqueue(item); return true; }
            public bool TryTake(out int item) { if (RejectTake) { item = 0; return false; } return _queue.TryDequeue(out item); }
            public int[] ToArray() => _queue.ToArray();
            public void CopyTo(int[] array, int index) => _queue.CopyTo(array, index);
            public void CopyTo(Array array, int index) => ((ICollection)_queue).CopyTo(array, index);
            public IEnumerator<int> GetEnumerator() => _queue.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Dispose() => Disposals++;
        }
        """;
}
