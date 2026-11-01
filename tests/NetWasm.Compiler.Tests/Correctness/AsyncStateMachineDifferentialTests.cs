namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AsyncStateMachineDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleaseAsyncStateMachinesMatchDesktopDotNet()
    {
        runner.Run(new(
            "AsyncStateMachineDifferential",
            "NetWasm.Correctness.AsyncStateMachines",
            """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace NetWasm.Correctness.AsyncStateMachines;

            public sealed class Resource : IAsyncDisposable
            {
                public ValueTask DisposeAsync()
                {
                    EntryPoint.AddTrace(1000);
                    return default;
                }
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    return Execute(input).Result;
                }

                public static int Trace() => _trace;

                public static void AddTrace(int value) => _trace += value;

                private static async Task<int> Execute(int input)
                {
                    await Task.CompletedTask;
                    await using var resource = new Resource();
                    var total = await Immediate(input);

                    var first = new TaskCompletionSource<int>();
                    var second = new TaskCompletionSource<int>();
                    var pending = AwaitBoth(first.Task, second.Task);
                    first.SetResult(input + 1);
                    second.SetResult(input + 2);
                    total += await pending;

                    await foreach (var value in Values(input))
                    {
                        total += value;
                    }

                    var canceled = new TaskCompletionSource<int>();
                    canceled.SetCanceled();
                    try
                    {
                        total += await canceled.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        _trace += 10;
                    }

                    var failed = new TaskCompletionSource<int>();
                    failed.SetException(new InvalidOperationException());
                    try
                    {
                        total += await failed.Task;
                    }
                    catch (InvalidOperationException)
                    {
                        _trace += 100;
                    }
                    _trace += total;
                    return total;
                }

                private static async Task<int> AwaitBoth(
                    Task<int> first,
                    Task<int> second)
                {
                    var left = await first;
                    var right = await second;
                    return left + right;
                }

                private static async ValueTask<int> Immediate(int value)
                {
                    await Task.CompletedTask;
                    return value + 3;
                }

                private static async IAsyncEnumerable<int> Values(int input)
                {
                    yield return input;
                    await Task.CompletedTask;
                    if (input < 0) yield break;
                    yield return input + 1;
                }
            }
            """,
            [-1, 0, 1, 41])
        { RequiresReactor = true });
    }
}
