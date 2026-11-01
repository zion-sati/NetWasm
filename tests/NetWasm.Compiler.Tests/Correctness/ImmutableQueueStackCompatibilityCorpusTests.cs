namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ImmutableQueueStackCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableQueueAndStackMatchDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "ImmutableQueueStackCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableQueueStack",
            Source,
            [0, 1, 2, 3, 4, 5, 6])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace NetWasm.Correctness.ImmutableQueueStack;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => StackOperations(),
                1 => QueueOperations(),
                2 => ImmutableSnapshots(),
                3 => InterfaceOperations(),
                4 => EmptyContracts(),
                5 => EmptyMessages(),
                6 => EnumeratorContracts(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int StackOperations()
            {
                var stack = ImmutableStack.CreateRange(new[] { 1, 2, 3 });
                var enumerator = stack.GetEnumerator();
                var direct = 0;
                while (enumerator.MoveNext())
                {
                    direct = direct * 10 + enumerator.Current;
                }

                var interfaceValues = 0;
                foreach (var value in (IEnumerable<int>)stack)
                {
                    interfaceValues += value;
                }

                var popped = stack.Pop(out var top);
                return direct + interfaceValues * 10 + top * 100 + popped.Peek();
            }

            private static int QueueOperations()
            {
                var queue = ImmutableQueue.CreateRange(new[] { 1, 2, 3 });
                var enumerator = queue.GetEnumerator();
                var direct = 0;
                while (enumerator.MoveNext())
                {
                    direct = direct * 10 + enumerator.Current;
                }

                var interfaceValues = 0;
                foreach (var value in (IEnumerable<int>)queue)
                {
                    interfaceValues += value;
                }

                var dequeued = queue.Dequeue(out var head);
                return direct + interfaceValues * 10 + head * 100 + dequeued.Peek();
            }

            private static int ImmutableSnapshots()
            {
                var emptyStack = ImmutableStack<int>.Empty;
                var pushedStack = emptyStack.Push(7).Push(8);
                var poppedStack = pushedStack.Pop();
                var emptyQueue = ImmutableQueue<int>.Empty;
                var enqueuedQueue = emptyQueue.Enqueue(9).Enqueue(10);
                var dequeuedQueue = enqueuedQueue.Dequeue();
                return emptyStack.IsEmpty && emptyQueue.IsEmpty &&
                    pushedStack.Peek() == 8 && poppedStack.Peek() == 7 &&
                    enqueuedQueue.Peek() == 9 && dequeuedQueue.Peek() == 10
                    ? 1
                    : -1;
            }

            private static int InterfaceOperations()
            {
                IImmutableStack<int> stack = ImmutableStack.Create(4);
                stack = stack.Push(5);
                stack = stack.Pop();
                IImmutableQueue<int> queue = ImmutableQueue.Create(6);
                queue = queue.Enqueue(7);
                queue = queue.Dequeue();
                return stack.Peek() * 10 + queue.Peek();
            }

            private static int EmptyContracts()
            {
                var stackPeek = ThrowsInvalidOperation(() =>
                    ImmutableStack<int>.Empty.Peek());
                var queuePeek = ThrowsInvalidOperation(() =>
                    ImmutableQueue<int>.Empty.Peek());
                var stackNull = ThrowsArgumentNull(() =>
                    ImmutableStack.CreateRange<int>(null!));
                var queueNull = ThrowsArgumentNull(() =>
                    ImmutableQueue.CreateRange<int>(null!));
                var stackFactory = ImmutableStack.Create(11, 12);
                var queueFactory = ImmutableQueue.Create(13, 14);
                return stackPeek && queuePeek && stackNull && queueNull &&
                    stackFactory.Peek() == 12 && queueFactory.Peek() == 13
                    ? 1
                    : -1;
            }

            private static int EmptyMessages()
            {
                const string Expected = "This operation does not apply to an empty instance.";
                var matches = 0;
                try { _ = ImmutableStack<int>.Empty.Peek(); }
                catch (InvalidOperationException ex) { if (ex.Message == Expected) matches++; }
                try { _ = ImmutableStack<int>.Empty.Pop(); }
                catch (InvalidOperationException ex) { if (ex.Message == Expected) matches++; }
                try { _ = ImmutableQueue<int>.Empty.Peek(); }
                catch (InvalidOperationException ex) { if (ex.Message == Expected) matches++; }
                try { _ = ImmutableQueue<int>.Empty.Dequeue(); }
                catch (InvalidOperationException ex) { if (ex.Message == Expected) matches++; }
                return matches;
            }

            private static int EnumeratorContracts()
            {
                var score = 0;
                var stack = ((IEnumerable<int>)ImmutableStack.Create(1, 2)).GetEnumerator();
                if (ThrowsInvalidCurrent(stack)) score++;
                if (stack.MoveNext() && stack.Current == 2) score++;
                ((System.Collections.IEnumerator)stack).Reset();
                if (stack.MoveNext() && stack.Current == 2) score++;
                stack.Dispose();
                if (ThrowsDisposed(stack)) score++;

                var queue = ((IEnumerable<int>)ImmutableQueue.Create(3, 4)).GetEnumerator();
                if (ThrowsInvalidCurrent(queue)) score++;
                if (queue.MoveNext() && queue.Current == 3) score++;
                ((System.Collections.IEnumerator)queue).Reset();
                if (queue.MoveNext() && queue.Current == 3) score++;
                queue.Dispose();
                if (ThrowsDisposed(queue)) score++;

                return score;
            }

            private static bool ThrowsInvalidCurrent(IEnumerator<int> enumerator)
            {
                try { _ = enumerator.Current; return false; }
                catch (InvalidOperationException) { return true; }
            }

            private static bool ThrowsDisposed(IEnumerator<int> enumerator)
            {
                try { _ = enumerator.MoveNext(); return false; }
                catch (ObjectDisposedException) { return true; }
            }

            private static bool ThrowsInvalidOperation(Func<int> operation)
            {
                try
                {
                    _ = operation();
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }

            private static bool ThrowsArgumentNull(Func<object?> operation)
            {
                try
                {
                    _ = operation();
                    return false;
                }
                catch (ArgumentNullException)
                {
                    return true;
                }
            }
        }
        """;
}
