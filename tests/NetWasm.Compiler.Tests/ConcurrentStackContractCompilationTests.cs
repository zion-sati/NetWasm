using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConcurrentStackContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StackOperationsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ConcurrentStackFixture", Source, "ConcurrentStackFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        namespace ConcurrentStackFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var stack = new ConcurrentStack<int>(new[] { 1, 2, 3 });
                if (stack.Count != 3 || stack.IsEmpty || !stack.TryPeek(out var peek) || peek != 3) return 1;
                var snapshot = stack.GetEnumerator();
                stack.Push(4);
                if (!snapshot.MoveNext() || snapshot.Current != 3) return 2;
                stack.Clear();
                if (!snapshot.MoveNext() || snapshot.Current != 2 || !snapshot.MoveNext() || snapshot.Current != 1 || snapshot.MoveNext()) return 3;
                snapshot.Dispose();
                if (!stack.IsEmpty || stack.TryPop(out var missing) || missing != 0 || stack.TryPeek(out missing) || missing != 0) return 4;
                stack.PushRange(new[] { 10, 20, 30, 40 }, 1, 2);
                stack.PushRange(new[] { 41, 42 });
                if (!Equal(stack.ToArray(), new[] { 42, 41, 30, 20 })) return 5;
                var destination = new[] { -1, -1, -1, -1, -1 };
                if (stack.TryPopRange(destination, 1, 3) != 3 || !Equal(destination, new[] { -1, 42, 41, 30, -1 })) return 6;
                if (stack.TryPopRange(destination) != 1 || destination[0] != 20 || !stack.IsEmpty) return 7;
                IProducerConsumerCollection<int> producer = stack;
                if (!producer.TryAdd(42) || !producer.TryTake(out var value) || value != 42 || producer.TryTake(out value)) return 8;
                stack.Push(20); stack.Push(22);
                var typed = new int[4]; stack.CopyTo(typed, 1);
                if (!Equal(typed, new[] { 0, 22, 20, 0 })) return 9;
                var boxed = new object[3]; ((ICollection)stack).CopyTo(boxed, 1);
                if (!Equals(boxed[1], 22) || !Equals(boxed[2], 20) || ((ICollection)stack).IsSynchronized) return 10;
                var untyped = ((IEnumerable)stack).GetEnumerator(); stack.Clear();
                if (!untyped.MoveNext() || !Equals(untyped.Current, 22) || !untyped.MoveNext() || !Equals(untyped.Current, 20) || untyped.MoveNext()) return 11;
                if (((IReadOnlyCollection<int>)stack).Count != 0) return 12;
                stack.PushRange(Array.Empty<int>(), 0, 0);
                if (stack.TryPopRange(Array.Empty<int>(), 0, 0) != 0) return 13;
                var strings = new ConcurrentStack<string?>(); strings.Push(null);
                if (!strings.TryPop(out var text) || text != null) return 14;
                try { _ = ((ICollection)stack).SyncRoot; return 15; } catch (NotSupportedException) { }
                try { new ConcurrentStack<int>(null!); return 16; } catch (ArgumentNullException ex) { if (ex.ParamName != "collection") return 17; }
                try { stack.PushRange(null!); return 18; } catch (ArgumentNullException ex) { if (ex.ParamName != "items") return 19; }
                try { stack.TryPopRange(null!); return 20; } catch (ArgumentNullException ex) { if (ex.ParamName != "items") return 21; }
                try { stack.PushRange(new int[1], -1, 0); return 22; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "startIndex") return 23; }
                try { stack.TryPopRange(new int[1], 0, -1); return 24; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "count") return 25; }
                try { stack.PushRange(new int[1], 1, 1); return 26; } catch (ArgumentException) { }
                try { stack.TryPopRange(new int[1], int.MaxValue, 1); return 27; } catch (ArgumentOutOfRangeException) { }
                try { stack.CopyTo(null!, 0); return 28; } catch (ArgumentNullException ex) { if (ex.ParamName != "array") return 29; }
                stack.Push(42);
                try { stack.CopyTo(Array.Empty<int>(), 0); return 30; } catch (ArgumentException) { }
                if (!stack.TryPop(out value) || value != 42) return 31;
                return input + 1;
            }
            private static bool Equal(int[] left, int[] right)
            {
                if (left.Length != right.Length) return false;
                for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
                return true;
            }
        }
        """;
}
