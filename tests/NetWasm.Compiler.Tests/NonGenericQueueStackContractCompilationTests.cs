using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NonGenericQueueStackContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CollectionsPreserveOrderingCloningAndMutationContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "CollectionFixture", Source, "CollectionFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        namespace CollectionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var q = new Queue(2, 1.5f);
                q.Enqueue(1); q.Enqueue(2);
                if ((int)q.Dequeue() != 1) return 1;
                q.Enqueue(3); q.Enqueue(null); q.Enqueue(4);
                if (q.Count != 4 || (int)q.Peek() != 2 || !q.Contains(null) || q.Contains(99)) return 2;
                var qc = (Queue)q.Clone(); q.Dequeue();
                if (qc.Count != 4 || (int)qc.Dequeue() != 2 || q.Count != 3) return 3;
                q.TrimToSize(); q.Enqueue(5);
                var a = q.ToArray();
                if ((int)a[0] != 3 || a[1] != null || (int)a[3] != 5) return 4;
                var e = q.GetEnumerator();
                try { var bad = e.Current; return 5; } catch (InvalidOperationException) { }
                if (!e.MoveNext() || (int)e.Current != 3) return 6;
                var ec = (IEnumerator)((ICloneable)e).Clone();
                if (!e.MoveNext() || e.Current != null || (int)ec.Current != 3) return 7;
                q.Enqueue(6);
                try { ec.MoveNext(); return 8; } catch (InvalidOperationException) { }
                try { e.Reset(); return 9; } catch (InvalidOperationException) { }
                var sq = Queue.Synchronized(q);
                if (!sq.IsSynchronized || !ReferenceEquals(sq.SyncRoot, q.SyncRoot)) return 10;
                sq.Dequeue(); if (q.Count != 4) return 11;
                var sqc = (Queue)sq.Clone(); sq.Clear();
                if (!sqc.IsSynchronized || sqc.Count != 4 || q.Count != 0) return 12;
                q.TrimToSize(); q.Enqueue(42); if ((int)q.Dequeue() != 42) return 13;
                try { q.Dequeue(); return 14; } catch (InvalidOperationException) { }
                try { new Queue(-1); return 15; } catch (ArgumentOutOfRangeException) { }
                try { new Queue(0, 0.5f); return 16; } catch (ArgumentOutOfRangeException) { }
                var s = new Stack(new object[] { 1, 2, null });
                if (s.Peek() != null || !s.Contains(2) || !s.Contains(null)) return 17;
                s.Pop(); for (var i = 3; i <= 20; i++) s.Push(i);
                var sc = (Stack)s.Clone();
                if ((int)s.Pop() != 20 || (int)sc.Peek() != 20) return 18;
                var dst = new int[20]; s.CopyTo(dst, 1);
                if (dst[1] != 19 || dst[19] != 1) return 19;
                var se = s.GetEnumerator(); se.MoveNext();
                var sec = (IEnumerator)((ICloneable)se).Clone(); se.MoveNext();
                if ((int)se.Current != 18 || (int)sec.Current != 19) return 20;
                se.Reset(); if (!se.MoveNext() || (int)se.Current != 19) return 21;
                var ss = Stack.Synchronized(s);
                if (!ss.IsSynchronized || !ReferenceEquals(ss.SyncRoot, s.SyncRoot)) return 22;
                ss.Push(22); if ((int)s.Peek() != 22) return 23;
                try { sec.MoveNext(); return 24; } catch (InvalidOperationException) { }
                var ssc = (Stack)ss.Clone(); ss.Clear();
                if (!ssc.IsSynchronized || ssc.Count != 20 || s.Count != 0) return 25;
                try { s.Peek(); return 26; } catch (InvalidOperationException) { }
                var empty = s.GetEnumerator();
                if (empty.MoveNext()) return 27;
                try { var bad = empty.Current; return 28; } catch (InvalidOperationException) { }
                var q2 = new Queue(new object[] { 1, 2 });
                var copied = new object[3]; q2.CopyTo(copied, 1);
                if ((int)copied[1] != 1 || (int)copied[2] != 2) return 29;
                try { q2.CopyTo(null, 0); return 30; } catch (ArgumentNullException) { }
                try { s.CopyTo(new object[1], -1); return 31; } catch (ArgumentOutOfRangeException) { }
                try { Stack.Synchronized(null); return 32; } catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        """;
}
