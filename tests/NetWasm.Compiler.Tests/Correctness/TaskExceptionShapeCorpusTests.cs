using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Task<T>.Result throws a faulted task's exceptions in an AggregateException, and a
// TaskCanceledException in one for a canceled task, while awaiting rethrows them unwrapped.
// WhenAll gathers its children's exceptions without flattening an AggregateException a child
// was faulted with. Each case returns a hash of the exception types and counts it observed.
public sealed class TaskExceptionShapeCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void ResultAndWhenAllExceptionShapesMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "TaskExceptionShapeCorpus",
            "NetWasm.Correctness.TaskExceptionShapes",
            Source,
            [.. Enumerable.Range(0, 14)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace NetWasm.Correctness.TaskExceptionShapes;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Shape(() => _ = Task.FromException<int>(new InvalidOperationException()).Result),
                    1 => Shape(() => Task.FromException<int>(new InvalidOperationException()).GetAwaiter().GetResult()),
                    2 => Shape(() => _ = Task.FromCanceled<int>(Canceled()).Result),
                    3 => Shape(() => Task.FromCanceled<int>(Canceled()).GetAwaiter().GetResult()),
                    4 => Shape(() => _ = Fails().Result),
                    5 => Shape(() => _ = Cancels().Result),
                    6 => Count(Task.WhenAll(Task.FromException(new AggregateException(new ArgumentException(), new FormatException())), Task.CompletedTask)),
                    7 => Shape(() => Task.WhenAll(Task.FromException(new AggregateException(new ArgumentException(), new FormatException())), Task.CompletedTask).GetAwaiter().GetResult()),
                    8 => Count(Task.WhenAll(Task.FromException(new ArgumentException()), Task.FromException(new FormatException()))),
                    9 => Count(Task.WhenAll(Task.FromException<int>(new AggregateException(new ArgumentException())), Task.FromResult(1))),
                    10 => Shape(() => _ = Task.WhenAll(Task.FromException<int>(new ArgumentException()), Task.FromResult(1)).Result),
                    11 => Shape(() => _ = Task.WhenAll(Task.FromCanceled<int>(Canceled()), Task.FromResult(1)).Result),
                    12 => Count(Several()),
                    13 => Shape(() => _ = Task.FromResult(42).Result),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static CancellationToken Canceled()
                {
                    var source = new CancellationTokenSource();
                    source.Cancel();
                    return source.Token;
                }

                private static async Task<int> Fails()
                {
                    await Task.CompletedTask;
                    throw new FormatException();
                }

                private static async Task<int> Cancels()
                {
                    await Task.CompletedTask;
                    throw new OperationCanceledException();
                }

                private static Task Several()
                {
                    var source = new TaskCompletionSource();
                    source.SetException(new Exception[] { new ArgumentException(), new AggregateException(new FormatException()) });
                    return source.Task;
                }

                private static int Count(Task task)
                {
                    try
                    {
                        task.GetAwaiter().GetResult();
                    }
                    catch
                    {
                    }
                    var exception = task.Exception;
                    if (exception is null)
                    {
                        return task.IsCanceled ? 1 : 0;
                    }
                    var hash = 17 * 31 + exception.InnerExceptions.Count;
                    foreach (var inner in exception.InnerExceptions)
                    {
                        hash = unchecked(hash * 31 + Kind(inner));
                    }
                    return hash;
                }

                private static int Shape(Action action)
                {
                    try
                    {
                        action();
                        return 0;
                    }
                    catch (AggregateException aggregate)
                    {
                        var hash = 1000 + aggregate.InnerExceptions.Count;
                        foreach (var inner in aggregate.InnerExceptions)
                        {
                            hash = unchecked(hash * 31 + Kind(inner));
                        }
                        return hash;
                    }
                    catch (Exception exception)
                    {
                        return 2000 + Kind(exception);
                    }
                }

                private static int Kind(Exception exception) => exception switch
                {
                    TaskCanceledException => 1,
                    OperationCanceledException => 2,
                    AggregateException => 3,
                    ArgumentException => 4,
                    FormatException => 5,
                    InvalidOperationException => 6,
                    _ => 9,
                };
            }
            """;
}
