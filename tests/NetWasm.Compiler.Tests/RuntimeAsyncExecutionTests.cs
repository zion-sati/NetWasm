// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Runtime-async methods that really suspend: async Main, compiled by Roslyn with runtime-async=on and lowered by
// NetWasm, runs as a WASI 0.3 command whose event loop resumes the suspended frames.
public sealed class RuntimeAsyncExecutionTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        // A value type's async method works on a copy of the instance, as desktop .NET's: the caller's stays as it was.
        public struct ValueCounter
        {
            public int Value;

            public async Task<int> AddAsync(int amount)
            {
                Value += amount;
                await Task.Yield();
                Value += amount;
                return Value;
            }
        }

        public sealed class Counter
        {
            private int _count;

            public async Task<int> Add(int value)
            {
                await Task.Yield();
                _count += value;
                return _count;
            }
        }

        // An awaitable whose awaiter has only INotifyCompletion, so Roslyn awaits it with AwaitAwaiter.
        public readonly struct Later
        {
            public Awaiter GetAwaiter() => new();

            public readonly struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => false;
                public void OnCompleted(Action continuation) => Task.Yield().GetAwaiter().OnCompleted(continuation);
                public int GetResult() => 7;
            }
        }

        public sealed class Resource : IDisposable
        {
            public static int Disposed;

            public void Dispose() => Disposed++;
        }

        public static class Program
        {
            private static int s_finallies;

            // Awaits inside using, try/finally and try/catch: a suspension leaves without running the finally blocks,
            // a resumption enters each block at its start, and an exception thrown after resuming reaches its catch.
            private static async Task<int> Protected(int value)
            {
                using (new Resource())
                {
                    try
                    {
                        for (var index = 0; index < 2; index++)
                        {
                            try
                            {
                                value += await Step(index);
                                await Fail();
                            }
                            catch (InvalidOperationException)
                            {
                                value += 10;
                            }
                        }
                    }
                    finally
                    {
                        s_finallies++;
                    }
                }
                return value;
            }

            private static int s_ranged;

            // A protected block holding the awaits after the first: its states, 1 and 2, are a range the dispatch tests
            // with `state - 1 <= 1` (unsigned).
            private static async Task<int> Ranged(int value)
            {
                var total = await Step(value);
                try
                {
                    total += await Step(value);
                    total += await Step(value);
                }
                finally
                {
                    s_ranged++;
                }
                return total;
            }

            // An async method with no await: its state machine never caches the state.
        #pragma warning disable CS1998
            private static async Task<int> Immediate(int value) => value + 1;
        #pragma warning restore CS1998

            private static async Task<T> Echo<T>(T value)
            {
                await Task.Delay(1).ConfigureAwait(false);
                return value;
            }

            private static async ValueTask Pause() => await Task.Yield();

            private static async Task<int> Step(int value)
            {
                await Task.Yield();
                return value + 1;
            }

            private static async ValueTask<string> Label(int value)
            {
                var text = value.ToString();
                await Task.Yield();
                return "total " + text;
            }

            private static int Four() => 4;

            private static string Name() => "mix";

            private static int Digits(int hundreds, int tens, long ones) => (int)(hundreds * 100 + tens * 10 + ones);

            private static string Mix(string name, int tens, long ones) => name + (tens * 10 + ones);

            // Scalars of every stack kind live across a suspension: saved as their bits, not boxed.
            private static async Task<int> Scalars(double factor)
            {
                var d = factor * 1.5;
                var f = (float)factor * 2.25f;
                nint n = -3;
                var b = factor > 0;
                var c = 'x';
                uint u = 0xFFFF_FFFE;
                ulong l = ulong.MaxValue - 1;
                await Task.Yield();
                return d == 1.5 && f == 2.25f && n == -3 && b && c == 'x' && u == 0xFFFF_FFFE && l == ulong.MaxValue - 1
                    ? 0 : 100;
            }

            private static async Task<long> Ones()
            {
                await Task.Yield();
                return 7;
            }

            private static async Task Fail()
            {
                await Task.Yield();
                throw new InvalidOperationException("after resuming");
            }

            public static async Task<int> Main()
            {
                var total = 0;
                for (var index = 0; index < 5; index++)
                {
                    total += await Step(index);
                }
                var failing = Fail();
                await Task.Yield();
                var counter = new Counter();
                await counter.Add(2);
                total += await counter.Add(3) - 5;
                total += (await Echo("abc")).Length + await Echo(4) - 7;
                await Pause();
                total += await Scalars(1.0);
                var valueCounter = new ValueCounter { Value = 1 };
                total += await valueCounter.AddAsync(2) - 5 + valueCounter.Value - 1;
                total += await new Later() - 7;
                total += await Protected(1) - 24 + s_finallies - 1 + Resource.Disposed - 1;
                total += await Ranged(1) - 6 + s_ranged - 1 + await Immediate(4) - 5;
                // Optimized IL keeps total and the earlier arguments on the stack across these suspensions, in an order
                // the results depend on.
                total += Digits(Four(), 2, await Ones()) - 427 + (Mix(Name(), Four(), await Ones()) == "mix47" ? 0 : 100);
                Console.WriteLine(await Label(total));
                Console.WriteLine(failing.IsFaulted ? failing.Exception!.InnerException!.Message : "not faulted");
                return total == 15 ? 0 : 1;
            }
        }
        """;

    // ExecutionContext and SynchronizationContext across suspensions. A call leaves its caller's contexts as it found
    // them, a frame resumes under the context it suspended with (none while flow was suppressed), a frame that awaited
    // its callee on a SynchronizationContext of its own continues there with the callee's result or exception, and
    // AsyncLocal handlers see the changes desktop .NET reports. DesktopContextOutput is what desktop .NET 11.0.0 prints
    // for the same program compiled with runtime-async=on (runtime async, no state machines).
    private const string ContextSource = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        // A SynchronizationContext that counts what is posted to it.
        public sealed class CountingContext : SynchronizationContext
        {
            public static int Posts;

            public override void Post(SendOrPostCallback callback, object? state)
            {
                Posts++;
                base.Post(callback, state);
            }

            public override SynchronizationContext CreateCopy() => this;
        }

        public static class Program
        {
            private static readonly AsyncLocal<string?> s_value = new();
            private static readonly AsyncLocal<string?> s_noticed = new(change =>
                Console.WriteLine($"  notice {change.PreviousValue ?? "-"} -> {change.CurrentValue ?? "-"}{(change.ThreadContextChanged ? " (context)" : "")}"));

            // The caller's value flows in; the callee's own value is its own, before and after it resumes.
            private static async Task<string> Inner()
            {
                var seen = s_value.Value;
                s_value.Value = "inner";
                await Task.Yield();
                return seen + "/" + s_value.Value;
            }

            private static async Task<int> CompletesAtOnce()
            {
                s_value.Value = "sync";
                await Task.CompletedTask;
                return 1;
            }

            private static async Task Outer()
            {
                s_value.Value = "outer";
                var seen = await Inner();
                Console.WriteLine($"outer: inner saw {seen}, outer has {s_value.Value}");
            }

            // Leaves the context the event loop runs it on, so its caller's frame continues on its own.
            private static async Task<int> Elsewhere(bool fail)
            {
                await Task.Delay(1).ConfigureAwait(false);
                if (fail) throw new InvalidOperationException("from elsewhere");
                return 42;
            }

            private static async Task<string> OnContext(bool fail)
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                try
                {
                    var value = await Elsewhere(fail);
                    return $"value {value} on {(SynchronizationContext.Current is CountingContext ? "counting" : "other")}";
                }
                catch (InvalidOperationException error)
                {
                    return $"caught {error.Message}";
                }
            }

            private static async Task<string> Noticing()
            {
                s_noticed.Value = "a";
                await Task.Yield();
                s_noticed.Value = "b";
                return s_noticed.Value;
            }

            public static async Task<int> Main()
            {
                s_value.Value = "main";
                var pending = Inner();
                Console.WriteLine($"after the call: {s_value.Value}");
                Console.WriteLine($"inner: {await pending}");
                Console.WriteLine($"after the await: {s_value.Value}");
                await CompletesAtOnce();
                Console.WriteLine($"after a synchronous completion: {s_value.Value}");
                await Outer();
                Console.WriteLine($"after outer: {s_value.Value}");

                Task<string> suppressed;
                using (ExecutionContext.SuppressFlow())
                {
                    suppressed = Inner();
                }
                Console.WriteLine($"suppressed: {await suppressed}");

                Console.WriteLine(await OnContext(fail: false));
                Console.WriteLine(await OnContext(fail: true));
                Console.WriteLine($"posts: {CountingContext.Posts}");
                Console.WriteLine($"context after: {(SynchronizationContext.Current is null ? "none" : "set")}");

                Console.WriteLine("noticing:");
                // Called, then awaited: Main's chain resumes nested in Noticing's dispatcher, which puts its own context
                // back when Main next suspends; awaited at once, the call is direct and Noticing's frame joins Main's chain.
                var noticing = Noticing();
                GC.KeepAlive(noticing);
                Console.WriteLine($"  returned {await noticing}");
                Console.WriteLine($"  main sees {s_noticed.Value ?? "-"}");
                Console.WriteLine("noticing, awaited at once:");
                Console.WriteLine($"  returned {await Noticing()}");
                Console.WriteLine($"  main sees {s_noticed.Value ?? "-"}");
                return 0;
            }
        }
        """;

    private static readonly string[] DesktopContextOutput =
    [
        "after the call: main",
        "inner: main/inner",
        "after the await: main",
        "after a synchronous completion: main",
        "outer: inner saw outer/inner, outer has outer",
        "after outer: main",
        "suppressed: main/",
        "value 42 on other",
        "caught from elsewhere",
        "posts: 2",
        "context after: none",
        "noticing:",
        "  notice - -> a",
        "  notice a -> - (context)",
        "  notice - -> a (context)",
        "  notice a -> b",
        "  notice b -> - (context)",
        "  returned b",
        "  main sees -",
        "noticing, awaited at once:",
        "  notice - -> a",
        "  notice a -> b (context)",
        "  notice b -> - (context)",
        "  notice - -> a (context)",
        "  notice a -> b",
        "  notice b -> - (context)",
        "  returned b",
        "  main sees -",
    ];

    // Compiled to state machines (stateMachines), desktop .NET notifies two more context changes (each machine's Start
    // restores the caller's context); rewritten into runtime async, the program notifies as runtime async does.
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task ContextsFlowAcrossSuspensionsAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncContexts", ContextSource, TestAssets.RepositorySdkVersion,
            "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(DesktopContextOutput, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Optimized IL keeps values on the evaluation stack across awaits, which the lowering spills.
    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.Oz, false, true)]
    [InlineData(FinalWasmOptimization.Oz, true, true)]
    public async Task SuspendedFramesResumeInOrderAndFaultTheirTasks(FinalWasmOptimization optimization, bool optimizedIl, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncProgram", Source, TestAssets.RepositorySdkVersion,
            "latest", optimizedIl, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["total 15", "after resuming"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Async methods with more parameters than a frame's resumer delegate takes (Func<T1, ..., T16, TResult>) resume
    // through a resumption stub the compiler provides: static, instance, on a struct, generic, and a plain Task. Each
    // suspends and reads its arguments after resuming. Desktop .NET prints the same.
    private const string WideSource = """
        using System;
        using System.Threading.Tasks;

        public static class Program
        {
            private static int s_total;

            private static async Task<long> Wide(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17, string label)
            {
                await Task.Yield();
                long sum = a1 * 1 + a2 * 2 + a3 * 3 + a4 * 4 + a5 * 5 + a6 * 6 + a7 * 7 + a8 * 8 + a9 * 9 + a10 * 10 + a11 * 11 + a12 * 12 + a13 * 13 + a14 * 14 + a15 * 15 + a16 * 16 + a17 * 17;
                await Task.Yield();
                return sum * 1000 + label.Length;
            }

            private sealed class Holder(int start)
            {
                public async ValueTask<int> Wide(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17)
                {
                    await Task.Yield();
                    return start + a1 - a17 + a9;
                }
            }

            private struct Counter
            {
                public int Base;

                public async Task<int> Wide(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17)
                {
                    await Task.Yield();
                    return Base * 10 + a17 + a16;
                }
            }

            private static async Task<string> Generic<T>(T a1, T a2, T a3, T a4, T a5, T a6, T a7, T a8, T a9, T a10, T a11, T a12, T a13, T a14, T a15, T a16, T a17)
            {
                await Task.Yield();
                return $"{a1}:{a9}:{a17}";
            }

            private static async Task Plain(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17)
            {
                await Task.Yield();
                s_total += a17 * 100 + a1;
            }

            public static async Task Main()
            {
                Console.WriteLine(await Wide(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, "label"));
                Console.WriteLine(await new Holder(100).Wide(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17));
                var counter = new Counter { Base = 4 };
                Console.WriteLine(await counter.Wide(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17));
                Console.WriteLine(await Generic("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q"));
                await Plain(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17);
                Console.WriteLine(s_total);
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    [InlineData(FinalWasmOptimization.Oz, true, true)]
    public async Task MethodsWithMoreParametersThanAResumerTakesResumeThroughAStub(FinalWasmOptimization optimization,
        bool optimizedIl, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncWide", WideSource, TestAssets.RepositorySdkVersion,
            "latest", optimizedIl, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["1785005", "93", "73", "a:i:q", "1701"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // An async method that throws an OperationCanceledException ends canceled, as its builder ends it: awaiting the task
    // rethrows that exception and Wait throws a TaskCanceledException; any other exception faults it. So it does whether
    // the method throws before it first suspends (the lowered method returns the ended task) or after (the driver ends
    // it), for each task type, and an awaited direct call passes the exception itself on. Desktop .NET prints the same.
    private const string CancellationSource = """
        using System;
        using System.Threading.Tasks;

        public static class Program
        {
            private static readonly OperationCanceledException s_thrown = new();

            private static async Task<int> CancelsInt(bool suspend)
            {
                if (suspend) await Task.Yield();
                throw s_thrown;
            }

            private static async Task Cancels(bool suspend)
            {
                if (suspend) await Task.Yield();
                throw s_thrown;
            }

            private static async ValueTask<int> CancelsIntValue(bool suspend)
            {
                if (suspend) await Task.Yield();
                throw s_thrown;
            }

            private static async ValueTask CancelsValue(bool suspend)
            {
                if (suspend) await Task.Yield();
                throw s_thrown;
            }

            private static async Task<int> Fails(bool suspend)
            {
                if (suspend) await Task.Yield();
                throw new FormatException();
            }

            private static string Kind(Exception exception) => exception switch
            {
                _ when ReferenceEquals(exception, s_thrown) => "the thrown exception",
                TaskCanceledException => "TaskCanceledException",
                OperationCanceledException => "OperationCanceledException",
                FormatException => "FormatException",
                _ => "other",
            };

            private static async Task Report(string name, Task task)
            {
                string awaited;
                try
                {
                    await task;
                    awaited = "completed";
                }
                catch (Exception exception)
                {
                    awaited = Kind(exception);
                }
                string waited;
                try
                {
                    task.Wait();
                    waited = "completed";
                }
                catch (AggregateException aggregate)
                {
                    waited = Kind(aggregate.InnerExceptions[0]);
                }
                Console.WriteLine(name + ": " + (task.IsCanceled ? "canceled" : task.IsFaulted ? "faulted" : "other") +
                    ", await " + awaited + ", Wait " + waited);
            }

            public static async Task<int> Main()
            {
                foreach (var suspend in new[] { false, true })
                {
                    var when = suspend ? " after suspending" : " before suspending";
                    await Report("Task<int>" + when, CancelsInt(suspend));
                    await Report("Task" + when, Cancels(suspend));
                    await Report("ValueTask<int>" + when, CancelsIntValue(suspend).AsTask());
                    await Report("ValueTask" + when, CancelsValue(suspend).AsTask());
                    await Report("faulting Task<int>" + when, Fails(suspend));
                    try
                    {
                        await CancelsInt(suspend);
                        Console.WriteLine("direct" + when + ": completed");
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("direct" + when + ": " + Kind(exception));
                    }
                }
                return 0;
            }
        }
        """;

    // Loops whose awaits can complete at once, in the shapes the structurer used to dispatch (the await's arms meet
    // only past the suspending arm's leave, so no join and no partitioning loop condition was found): Maybe completes
    // at once except for every third value, so each loop runs both paths. Desktop .NET 11 prints the expected values.
    private const string LoopSource = """
        using System;
        using System.Threading.Tasks;

        public static class Program
        {
            // Completes at once, except for every third value, which suspends first.
            private static async Task<int> Maybe(int value)
            {
                if (value % 3 == 0) await Task.Yield();
                return value * 2 + 1;
            }

            private static Task<int> Ready(int value) => Task.FromResult(value + 3);

            // The loop's only await can complete at once.
            private static async Task<long> OnlyAwait(int count)
            {
                long total = 0;
                for (var index = 0; index < count; index++) total += await Maybe(index);
                return total;
            }

            // A branch right after the await.
            private static async Task<long> BranchAfterAwait(int count)
            {
                long total = 0;
                var marks = 0;
                for (var index = 0; index < count; index++)
                {
                    var value = await Maybe(index);
                    if (value % 5 == 0) marks++;
                    total += value;
                }
                return total * 100 + marks;
            }

            // A continue that skips the loop's second await.
            private static async Task<long> ContinueAndSecondAwait(int count)
            {
                long total = 0;
                var index = 0;
                while (index < count)
                {
                    total += await Ready(index);
                    if (total % 7 == 0) total += 11;
                    index++;
                    if (total > 500) continue;
                    await Task.Yield();
                }
                return total;
            }

            // A loop left only by a break after the await.
            private static async Task<long> Break(int limit)
            {
                long total = 0;
                for (var index = 0; ; index++)
                {
                    var value = await Maybe(index);
                    if (value > limit) break;
                    total += value;
                }
                return total;
            }

            private static async Task<long> Nested(int count)
            {
                long total = 0;
                for (var outer = 0; outer < count; outer++)
                {
                    for (var inner = 0; inner < outer; inner++) total += await Maybe(inner + outer);
                }
                return total;
            }

            private static async Task<long> FinallyInLoop(int count)
            {
                long total = 0;
                var finallies = 0;
                for (var index = 0; index < count; index++)
                {
                    try
                    {
                        total += await Maybe(index);
                    }
                    finally
                    {
                        finallies++;
                    }
                }
                return total * 1000 + finallies;
            }

            private static async Task<long> CodeAfterLoop(int count)
            {
                long total = 0;
                var index = 0;
                while (index < count)
                {
                    total += await Ready(index);
                    index++;
                }
                total *= 3;
                total += await Maybe(count);
                return total;
            }

            private static async Task<long> TwoLoops(int count)
            {
                long first = 0;
                long second = 0;
                for (var index = 0; index < count; index++) first += await Ready(index);
                for (var index = 0; index < count; index++) second += await Maybe(index);
                return first * 10000 + second;
            }

            public static async Task<int> Main()
            {
                Console.WriteLine(await OnlyAwait(20));
                Console.WriteLine(await BranchAfterAwait(20));
                Console.WriteLine(await ContinueAndSecondAwait(60));
                Console.WriteLine(await Break(30));
                Console.WriteLine(await Nested(7));
                Console.WriteLine(await FinallyInLoop(9));
                Console.WriteLine(await CodeAfterLoop(10));
                Console.WriteLine(await TwoLoops(12));
                return 0;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    public async Task LoopsWhoseAwaitsCanCompleteAtOnceRunAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncLoopShapes", LoopSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["400", "40004", "1972", "225", "273", "81009", "246", "1020144"],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Protected blocks that start where an enclosing one does (Roslyn starts C#'s `try { try { } ... await ...; }` at one
    // offset) while the enclosing block holds an await outside them: System.Text.Json's pipe SerializeAsync, built with
    // runtime async, failed to compile (NW1002, the enclosing block's dispatch ran inside the nested block). Each shape
    // runs completing and faulting after it resumes; the log records which blocks ran, in order. Desktop .NET 11 prints
    // the expected lines.
    private const string NestedTrySource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class Program
        {
            private static readonly List<string> Log = [];

            public static async Task<int> Main()
            {
                foreach (var fault in new[] { false, true })
                {
                    await Report("none", () => NestedHoldsNoAwait(fault));
                    await Report("coalesce", () => Coalesced(fault ? null : "given", fault));
                    await Report("catch", () => NestedCatches(fault));
                    await Report("await", () => NestedHoldsAnAwait(fault));
                    await Report("loop", () => LoopAwaitsInItsFinally(fault));
                }
                return 0;
            }

            private static async Task Report(string name, Func<Task<int>> run)
            {
                Log.Clear();
                string result;
                try
                {
                    result = (await run()).ToString();
                }
                catch (InvalidOperationException exception)
                {
                    result = "threw " + exception.Message;
                }
                Console.WriteLine(name + " " + result + " " + string.Join(",", Log));
            }

            private static async Task<int> Step(int value)
            {
                Log.Add("step" + value);
                await Task.Yield();
                return value;
            }

            private static async Task<int> NestedHoldsNoAwait(bool fault)
            {
                var seed = 1;
                try
                {
                    try { seed += 10; Log.Add("inner"); }
                    finally { seed *= 2; Log.Add("inner-finally"); }
                    var result = await Step(seed);
                    if (fault) throw new InvalidOperationException("after " + result);
                    return result;
                }
                finally { Log.Add("outer-finally"); }
            }

            private static async Task<int> Coalesced(string? given, bool fault)
            {
                var text = given ?? "rented";
                try
                {
                    try { Log.Add(text); }
                    finally { if (given is null) Log.Add("returned"); }
                    var result = await Step(text.Length);
                    if (fault) throw new InvalidOperationException("after " + result);
                    return result;
                }
                finally { if (given is null) Log.Add("disposed"); }
            }

            private static async Task<int> NestedCatches(bool fault)
            {
                try
                {
                    try { if (fault) throw new ArgumentException("inner"); Log.Add("inner"); }
                    catch (ArgumentException exception) { Log.Add("caught " + exception.Message); }
                    var result = await Step(3);
                    if (fault) throw new InvalidOperationException("after " + result);
                    return result;
                }
                catch (InvalidOperationException exception) when (exception.Message.StartsWith("after", StringComparison.Ordinal))
                {
                    Log.Add("outer caught");
                    return -1;
                }
            }

            private static async Task<int> NestedHoldsAnAwait(bool fault)
            {
                var seed = 2;
                try
                {
                    try { seed += await Step(seed); }
                    finally { seed++; Log.Add("inner-finally"); }
                    var result = await Step(seed);
                    if (fault) throw new InvalidOperationException("after " + result);
                    return result;
                }
                finally { Log.Add("outer-finally"); }
            }

            private static async Task<int> LoopAwaitsInItsFinally(bool fault)
            {
                var result = 0;
                Task? pending = null;
                try
                {
                    var round = 0;
                    bool done;
                    do
                    {
                        try
                        {
                            done = ++round == 3;
                            if (round == 2) pending = Step(20);
                            result += await Step(round);
                            if (fault && round == 2) throw new InvalidOperationException("round " + round);
                        }
                        finally
                        {
                            if (pending is not null)
                            {
                                await pending;
                                pending = null;
                                Log.Add("pending");
                            }
                        }
                    }
                    while (!done);
                    return result;
                }
                finally { Log.Add("loop-finally"); }
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task NestedProtectedBlocksAtOneStartRunAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncNestedTries", NestedTrySource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(
            [
                "none 22 inner,inner-finally,step22,outer-finally",
                "coalesce 5 given,step5",
                "catch 3 inner,step3",
                "await 5 step2,inner-finally,step5,outer-finally",
                "loop 6 step1,step20,step2,pending,step3,loop-finally",
                "none threw after 22 inner,inner-finally,step22,outer-finally",
                "coalesce threw after 6 rented,returned,step6,disposed",
                "catch -1 caught inner,step3,outer caught",
                "await threw after 5 step2,inner-finally,step5,outer-finally",
                "loop threw round 2 step1,step20,step2,pending,loop-finally",
            ],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // A loop that awaits a read that can complete at once, after argument checks that throw (SharpCompress's
    // ReadExactAsync): a structurer retry, since removed, took the loop's head as the end of a path at O3, so the arm
    // that reached it ran into a trap and every read threw. The stream completes every read at once but every third,
    // which suspends. Desktop .NET 11 prints the expected lines.
    private const string ReadExactSource = """
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;

        // A stream whose reads complete at once except every third, which suspends first.
        public sealed class Chunked(byte[] data, int chunk) : Stream
        {
            private int _position;
            private int _reads;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => data.Length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                ++_reads % 3 == 0 ? Later(buffer) : new(Take(buffer));

            private async ValueTask<int> Later(Memory<byte> buffer)
            {
                await Task.Yield();
                return Take(buffer);
            }

            private int Take(Memory<byte> buffer)
            {
                var count = Math.Min(Math.Min(buffer.Length, chunk), data.Length - _position);
                data.AsSpan(_position, count).CopyTo(buffer.Span);
                _position += count;
                return count;
            }
        }

        public static class Program
        {
            private static async ValueTask ReadExactAsync(Stream source, byte[] buffer, int offset, int length,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(buffer);
                if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                if (length < 0 || length > buffer.Length - offset) throw new ArgumentOutOfRangeException(nameof(length));
                while (length > 0)
                {
                    var fetched = await source.ReadAsync(buffer.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);
                    if (fetched <= 0) throw new EndOfStreamException("Unexpected end of stream.");
                    offset += fetched;
                    length -= fetched;
                }
            }

            public static async Task<int> Main()
            {
                var data = new byte[100];
                for (var index = 0; index < data.Length; index++) data[index] = (byte)(index * 7);
                foreach (var (chunk, length) in new[] { (7, 100), (13, 64), (100, 100), (5, 120) })
                {
                    var buffer = new byte[120];
                    try
                    {
                        await ReadExactAsync(new Chunked(data, chunk), buffer, 10, length);
                        var sum = 0;
                        foreach (var value in buffer) sum += value;
                        Console.WriteLine($"{chunk} {length} read {sum}");
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"{chunk} {length} " + exception switch
                        {
                            ArgumentOutOfRangeException => "out of range",
                            EndOfStreamException => "end of stream",
                            _ => "unexpected",
                        });
                    }
                }
                return 0;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task ALoopReadingUntilItHasEnoughRunsAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncReadExact", ReadExactSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["7 100 read 11866", "13 64 read 7200", "100 100 read 11866", "5 120 out of range"],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Values Roslyn leaves on the evaluation stack under an await: an object read from a field of a type that is not
    // generic (which the decoder names by its definition), from a static field, a field's value in a compound assignment,
    // narrowed and widened integers, and a string; and the value-type field and array element Roslyn spills itself.
    // The object read from a field failed to compile (NW1001, its type unknown). Desktop .NET 11 prints the expected
    // lines.
    private const string SpillSource = """
        using System;
        using System.Threading.Tasks;

        public sealed class Codec { public int Available; public int Next; public string Name = "codec"; }

        public struct Pair { public int Left; public int Right; }

        public sealed class Reader
        {
            private readonly Codec _codec = new();
            private static readonly Codec Shared = new();
            private int _count;
            private Pair _pair;
            private readonly int[] _values = new int[4];

            private static async Task<int> Fill(int value)
            {
                await Task.Yield();
                return value;
            }

            public async Task<string> Run(int seed)
            {
                _codec.Next = 0;
                _codec.Available = await Fill(seed);
                Shared.Available = await Fill(seed + 1);
                _count += await Fill(2);
                var narrowed = (byte)(seed * 100) + await Fill(3);
                var wide = (long)seed * 1000 + await Fill(4);
                _codec.Name = _codec.Name + await Fill(5);
                _pair.Left = await Fill(seed);
                _pair.Right += await Fill(2);
                _values[seed % 4] += await Fill(3);
                return $"{_codec.Available} {Shared.Available} {_count} {narrowed} {wide} {_codec.Name} " +
                    $"{_pair.Left} {_pair.Right} {_values[seed % 4]}";
            }
        }

        public static class Program
        {
            public static async Task<int> Main()
            {
                var reader = new Reader();
                Console.WriteLine(await reader.Run(7));
                Console.WriteLine(await reader.Run(300));
                return 0;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task ValuesUnderAnAwaitAreKeptAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncSpills", SpillSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["7 8 2 191 7004 codec5 7 2 3", "300 301 4 51 300004 codec55 300 4 3"],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Values where two paths meet, left under an await (C#'s `flag ? null : box`, `flag ? aByte : 3`, two derived
    // classes, two strings, an enum constant or local), and a function pointer call in a runtime-async method. The
    // lowering's stack typer gave a slot two producers fill with different types no type, and did not follow a calli at
    // all, so both methods failed to compile (NW1001). Desktop .NET 11 prints the expected lines.
    private const string JoinSource = """
        using System;
        using System.Threading.Tasks;

        public class Base { public virtual int Value => 1; }
        public sealed class Left : Base { public override int Value => 10; }
        public sealed class Right : Base { public override int Value => 100; }
        public sealed class Box { public int Value = 5; }

        public enum Color { Red = 2, Green = 30, Blue = 400 }

        public static class Program
        {
            private static async Task<int> Step(int value)
            {
                await Task.Yield();
                return value;
            }

            private static int Take(Box? box, int value) => (box?.Value ?? -1) + value;

            private static int Take(Base item, int value) => item.Value + value;

            private static int Twice(int value) => value * 2;

            private static async Task<int> Joins(int seed)
            {
                var box = new Box();
                byte small = (byte)(seed + 200);
                var total = Take((seed & 1) == 0 ? null : box, await Step(seed));
                total += ((seed & 2) == 0 ? small : 3) + await Step(1);
                total += Take((seed & 4) == 0 ? new Left() : new Right(), await Step(2));
                total += ((seed & 1) == 0 ? "even" : "odd").Length + await Step(3);
                var color = (seed & 2) == 0 ? Color.Green : Color.Blue;
                total += (int)((seed & 1) == 0 ? Color.Red : color) + await Step(4);
                return total;
            }

            private static async Task<int> Pointer(int seed)
            {
                var value = await Step(seed);
                unsafe
                {
                    delegate*<int, int> twice = &Twice;
                    value += twice(value);
                }
                return value + await Step(1);
            }

            public static async Task<int> Main()
            {
                foreach (var seed in new[] { 0, 1, 2, 7 }) Console.WriteLine(await Joins(seed) + " " + await Pointer(seed));
                return 0;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task ValuesWherePathsMeetAndAFunctionPointerCallRunAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncJoins", JoinSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines), allowUnsafe: true);
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["225 1", "260 4", "30 7", "528 22"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly string[] CancellationMoments = [" before suspending", " after suspending"];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task AnOperationCanceledExceptionEndsTheTaskCanceledAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncCancellation", CancellationSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        string[] expected = [.. CancellationMoments.SelectMany(when => new[]
        {
            "Task<int>" + when + ": canceled, await the thrown exception, Wait TaskCanceledException",
            "Task" + when + ": canceled, await the thrown exception, Wait TaskCanceledException",
            "ValueTask<int>" + when + ": canceled, await the thrown exception, Wait TaskCanceledException",
            "ValueTask" + when + ": canceled, await the thrown exception, Wait TaskCanceledException",
            "faulting Task<int>" + when + ": faulted, await FormatException, Wait FormatException",
            "direct" + when + ": the thrown exception",
        })];
        Assert.Equal(expected, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // A direct call asks its callee to leave its result in the slot. Two things can run between the request and the
    // callee: a static constructor its declaring type runs before the call (here one that starts an async method and
    // keeps its task), and a null receiver's NullReferenceException, after which the program calls an async method for
    // its task. Neither may take the request meant for the callee. Desktop .NET prints the same.
    private const string DirectRequestSource = """
        using System;
        using System.Threading.Tasks;

        public static class Program
        {
            public static async Task<int> Main()
            {
                Console.WriteLine(await Initialized.Value());
                Console.WriteLine(Initialized.Started is null ? "no task" : (await Initialized.Started).ToString());
                Holder? holder = Holder.None();
                try
                {
                    Console.WriteLine(await holder!.Get());
                }
                catch (NullReferenceException)
                {
                    Console.WriteLine("null receiver");
                }
                var task = new Holder().Get();
                Console.WriteLine(task is null ? "no task" : (await task).ToString());
                return 0;
            }
        }

        public static class Initialized
        {
            public static readonly Task<int> Started;

            static Initialized()
            {
                Started = Start();
            }

            private static async Task<int> Start()
            {
                await Task.Yield();
                return 7;
            }

            public static async Task<int> Value()
            {
                await Task.Yield();
                return 5;
            }
        }

        public sealed class Holder
        {
            private readonly int _value = 9;

            public static Holder? None() => null;

            public async Task<int> Get()
            {
                await Task.Yield();
                return _value;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task ADirectCallsRequestReachesOnlyItsCalleeAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncDirectRequest", DirectRequestSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["5", "7", "null receiver", "9"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // A leaf await through ConfigureAwait (a task held in a local, a value task made in place or kept in a local of the
    // program's) takes the task itself and continues as the configured awaiter would: with true on its own
    // SynchronizationContext once the task completes elsewhere (posted there, so the count goes up), with false where
    // the task completed, nothing posted. A faulted task passes its exception through. Desktop .NET 11 prints the same.
    private const string ConfiguredLeafSource = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        public sealed class CountingContext : SynchronizationContext
        {
            public static int Posts;

            public override void Post(SendOrPostCallback callback, object? state)
            {
                Posts++;
                base.Post(callback, state);
            }

            public override SynchronizationContext CreateCopy() => this;
        }

        public static class Program
        {
            public static async Task<int> Main()
            {
                foreach (var continueOnContext in new[] { true, false })
                {
                    Console.WriteLine(await HeldTask(continueOnContext));
                    Console.WriteLine(await ValueMadeInPlace(continueOnContext));
                    Console.WriteLine(await UntypedMadeInPlace(continueOnContext));
                    Console.WriteLine(await ValueInALocal(continueOnContext));
                    Console.WriteLine($"posts: {CountingContext.Posts}");
                }
                Console.WriteLine(await Failing());
                return 0;
            }

            private static async Task<int> Elsewhere(bool fail)
            {
                await Task.Delay(1).ConfigureAwait(false);
                if (fail) throw new InvalidOperationException("from elsewhere");
                return 42;
            }

            private static async Task Later() => await Task.Delay(1).ConfigureAwait(false);

            private static async Task<string> HeldTask(bool continueOnContext)
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                var task = Elsewhere(false);
                var value = continueOnContext ? await task.ConfigureAwait(true) : await task.ConfigureAwait(false);
                return $"task {continueOnContext}: {value} on {Where()}";
            }

            private static async Task<string> ValueMadeInPlace(bool continueOnContext)
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                var value = continueOnContext
                    ? await new ValueTask<int>(Elsewhere(false)).ConfigureAwait(true)
                    : await new ValueTask<int>(Elsewhere(false)).ConfigureAwait(false);
                return $"value {continueOnContext}: {value} on {Where()}";
            }

            private static async Task<string> UntypedMadeInPlace(bool continueOnContext)
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                if (continueOnContext) await new ValueTask(Later()).ConfigureAwait(true);
                else await new ValueTask(Later()).ConfigureAwait(false);
                return $"untyped {continueOnContext}: on {Where()}";
            }

            private static async Task<string> ValueInALocal(bool continueOnContext)
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                ValueTask<int> pending = new ValueTask<int>(Elsewhere(false));
                var value = await pending.ConfigureAwait(continueOnContext);
                return $"local {continueOnContext}: {value} on {Where()}";
            }

            private static async Task<string> Failing()
            {
                var task = Elsewhere(true);
                try
                {
                    return $"{await task.ConfigureAwait(false)}";
                }
                catch (InvalidOperationException error)
                {
                    return $"caught {error.Message}";
                }
            }

            private static string Where() => SynchronizationContext.Current is CountingContext ? "counting" : "other";
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task ALeafAwaitThroughConfigureAwaitContinuesAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncConfiguredLeaf", ConfiguredLeafSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(
        [
            "task True: 42 on other", "value True: 42 on other", "untyped True: on other", "local True: 42 on other",
            "posts: 4",
            "task False: 42 on other", "value False: 42 on other", "untyped False: on other", "local False: 42 on other",
            "posts: 4",
            "caught from elsewhere",
        ], output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // An awaited call through ConfigureAwait is a direct call too. With true the frame continues on its own
    // SynchronizationContext once the callee completes elsewhere (posted there, so the count goes up; the base Post runs
    // the callback without making the context current); with false it continues where the callee completed, and nothing
    // is posted. A callee that faults passes its exception through either way. Desktop .NET 11 prints the same.
    private const string ConfiguredDirectSource = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        public sealed class CountingContext : SynchronizationContext
        {
            public static int Posts;

            public override void Post(SendOrPostCallback callback, object? state)
            {
                Posts++;
                base.Post(callback, state);
            }

            public override SynchronizationContext CreateCopy() => this;
        }

        public static class Program
        {
            public static async Task<int> Main()
            {
                Console.WriteLine(await OnContext());
                Console.WriteLine($"posts: {CountingContext.Posts}");
                Console.WriteLine(await OffContext());
                Console.WriteLine($"posts: {CountingContext.Posts}");
                Console.WriteLine(await Failing());
                return 0;
            }

            private static async Task<int> Elsewhere(bool fail)
            {
                await Task.Delay(1).ConfigureAwait(false);
                if (fail) throw new InvalidOperationException("from elsewhere");
                return 42;
            }

            private static async Task<string> OnContext()
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                var value = await Elsewhere(false).ConfigureAwait(true);
                return $"true: {value} on {Where()}";
            }

            private static async Task<string> OffContext()
            {
                SynchronizationContext.SetSynchronizationContext(new CountingContext());
                var value = await Elsewhere(false).ConfigureAwait(false);
                return $"false: {value} on {Where()}";
            }

            private static async Task<string> Failing()
            {
                try
                {
                    return $"{await Elsewhere(true).ConfigureAwait(false)}";
                }
                catch (InvalidOperationException error)
                {
                    return $"caught {error.Message}";
                }
            }

            private static string Where() => SynchronizationContext.Current is CountingContext ? "counting" : "other";
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public async Task AnAwaitThroughConfigureAwaitCallsDirectlyAndContinuesAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncConfiguredDirect", ConfiguredDirectSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["true: 42 on other", "posts: 1", "false: 42 on other", "posts: 1", "caught from elsewhere"],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Awaits of virtual and interface calls ask the override that runs to take the call directly. A lowered override
    // does, and its result comes through the slot; any other declines the request and returns its task, which the await
    // takes: completed, completed later, calling a lowered method of the same receiver for its task (base.ReadAsync), or
    // wrapping another source. Configured or not, faulting before or after suspending, a value task interface, value
    // tasks configured through the local Roslyn reuses, and a null receiver. Desktop .NET 11 prints the same.
    private const string VirtualDirectSource = """
        #nullable enable
        using System;
        using System.Threading.Tasks;

        public abstract class Source
        {
            public abstract Task<int> ReadAsync(int value);

            public virtual async Task<int> TwiceAsync(int value)
            {
                await Task.Yield();
                return value * 2;
            }
        }

        // Lowered: suspends, then completes.
        public sealed class Lowered : Source
        {
            public override async Task<int> ReadAsync(int value)
            {
                await Task.Yield();
                return value + 1;
            }
        }

        // Not lowered: a completed task.
        public sealed class Completed : Source
        {
            public override Task<int> ReadAsync(int value) => Task.FromResult(value + 2);
        }

        // Not lowered: completes later, through a TaskCompletionSource.
        public sealed class Later : Source
        {
            public override Task<int> ReadAsync(int value)
            {
                var source = new TaskCompletionSource<int>();
                _ = Complete(source, value);
                return source.Task;
            }

            private static async Task Complete(TaskCompletionSource<int> source, int value)
            {
                await Task.Yield();
                source.SetResult(value + 3);
            }
        }

        // Not lowered, calling a lowered method of the same receiver for its task.
        public class Base : Source
        {
            public override async Task<int> ReadAsync(int value)
            {
                await Task.Yield();
                return value + 4;
            }
        }

        public sealed class Forwarding : Base
        {
            public override Task<int> ReadAsync(int value)
            {
                var task = base.ReadAsync(value);
                return task.ContinueWith(done => done.Result * 10);
            }

            public override Task<int> TwiceAsync(int value) => base.TwiceAsync(value + 1);
        }

        // Not lowered, wrapping another source.
        public sealed class Wrapping(Source inner) : Source
        {
            public override Task<int> ReadAsync(int value) => inner.ReadAsync(value + 100);
        }

        // Lowered, faulting before and after it suspends.
        public sealed class Faulting : Source
        {
            public override async Task<int> ReadAsync(int value)
            {
                if (value < 0) throw new InvalidOperationException("before");
                await Task.Yield();
                throw new InvalidOperationException("after");
            }
        }

        public interface ICounter
        {
            ValueTask<int> NextAsync();
        }

        public sealed class LoweredCounter : ICounter
        {
            private int _count;

            public async ValueTask<int> NextAsync()
            {
                await Task.Yield();
                return ++_count;
            }
        }

        public sealed class PlainCounter : ICounter
        {
            private int _count = 40;

            public ValueTask<int> NextAsync() => new(++_count);
        }

        public static class Program
        {
            public static async Task<int> Main()
            {
                (string Name, Source Source)[] sources =
                [
                    ("Lowered", new Lowered()), ("Completed", new Completed()), ("Later", new Later()), ("Base", new Base()),
                    ("Forwarding", new Forwarding()), ("Wrapping", new Wrapping(new Lowered())), ("Wrapping", new Wrapping(new Later())),
                ];
                foreach (var (name, source) in sources)
                {
                    Console.WriteLine($"{name}: {await source.ReadAsync(1)} {await source.ReadAsync(2).ConfigureAwait(false)} {await source.TwiceAsync(3)}");
                }
                foreach (var value in new[] { -1, 1 })
                {
                    try
                    {
                        Console.WriteLine(await new Faulting().ReadAsync(value));
                    }
                    catch (InvalidOperationException error)
                    {
                        Console.WriteLine($"caught {error.Message}");
                    }
                }
                (string Name, ICounter Counter)[] counters = [("LoweredCounter", new LoweredCounter()), ("PlainCounter", new PlainCounter())];
                foreach (var (name, counter) in counters)
                {
                    var first = await counter.NextAsync();
                    var second = await counter.NextAsync().ConfigureAwait(false);
                    Console.WriteLine($"{name}: {first} {second}");
                }
                Console.WriteLine($"value task: {await Tripled(1).ConfigureAwait(false)} {await Tripled(2).ConfigureAwait(false)}");
                Source? missing = Missing();
                try
                {
                    Console.WriteLine(await missing!.ReadAsync(1));
                }
                catch (NullReferenceException)
                {
                    Console.WriteLine("null receiver");
                }
                var pending = new Lowered().ReadAsync(7);
                Console.WriteLine(pending is null ? "no task" : $"task {await pending}");
                return 0;
            }

            private static Source? Missing() => null;

            private static async ValueTask<int> Tripled(int value)
            {
                await Task.Yield();
                return value * 3;
            }
        }
        """;

    private static readonly string[] DesktopVirtualDirectOutput =
    [
        "Lowered: 2 3 6", "Completed: 3 4 6", "Later: 4 5 6", "Base: 5 6 6", "Forwarding: 50 60 8", "Wrapping: 102 103 6",
        "Wrapping: 104 105 6", "caught before", "caught after", "LoweredCounter: 1 2", "PlainCounter: 41 42",
        "value task: 3 6", "null receiver", "task 8",
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    public async Task VirtualAndInterfaceAwaitsTakeTheirCallsAsOnDesktop(FinalWasmOptimization optimization, bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncVirtualDirect", VirtualDirectSource,
            TestAssets.RepositorySdkVersion, "latest", true, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(DesktopVirtualDirectOutput, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // The same programs compiled without runtime-async=on: Roslyn's state machines, which the compiler rewrites into
    // runtime async methods (a struct's method keeps its machine).
    // Results of enums another assembly defines (CoreLib's DayOfWeek and StringComparison): an enum a signature names
    // through a type reference is a value type there, and only its definition says it is an integer. A method returning
    // ValueTask<DayOfWeek>, awaits of an interface's ValueTask<DayOfWeek> and Task<StringComparison>, a day live across
    // the awaits after it (a state machine's field of the enum's type), and generic methods instantiated over it, one
    // with more parameters than a resumer delegate takes.
    private const string ForeignEnumSource = """
        using System;
        using System.Threading.Tasks;

        public interface ISource
        {
            ValueTask<DayOfWeek> Day(int offset);
            Task<StringComparison> Comparison();
        }

        public sealed class Source : ISource
        {
            public async ValueTask<DayOfWeek> Day(int offset)
            {
                await Task.Yield();
                return (DayOfWeek)((int)DayOfWeek.Monday + offset);
            }

            public async Task<StringComparison> Comparison()
            {
                await Task.Yield();
                return StringComparison.OrdinalIgnoreCase;
            }
        }

        public static class Program
        {
            private static async ValueTask<DayOfWeek> Next(DayOfWeek day)
            {
                await Task.Yield();
                return (DayOfWeek)(((int)day + 1) % 7);
            }

            // Generic over the enum: its resumer is instantiated as the method is.
            private static async Task<T> Echo<T>(T value)
            {
                await Task.Yield();
                return value;
            }

            // More parameters than a resumer delegate takes: it resumes through a stub instantiated as the method is.
            private static async Task<T> Wide<T>(T a1, T a2, T a3, T a4, T a5, T a6, T a7, T a8, T a9, T a10, T a11, T a12,
                T a13, T a14, T a15, T a16, T a17)
            {
                await Task.Yield();
                return a17;
            }

            private static async Task<int> Walk(ISource source)
            {
                var day = await source.Day(2);
                var comparison = await source.Comparison();
                day = await Next(day);
                day = await Echo(day);
                day = await Wide(day, day, day, day, day, day, day, day, day, day, day, day, day, day, day, day, day);
                Console.WriteLine($"{(int)day} {(int)comparison}");
                return (int)day * 10 + (int)comparison;
            }

            public static async Task<int> Main()
            {
                var total = await Walk(new Source());
                Console.WriteLine(total);
                return total == 45 ? 0 : 1;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.None, false, true)]
    [InlineData(FinalWasmOptimization.Oz, true, true)]
    public async Task ResultsOfAnotherAssemblysEnumsAreIntegers(FinalWasmOptimization optimization, bool optimizedIl,
        bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncForeignEnums", ForeignEnumSource,
            TestAssets.RepositorySdkVersion, "latest", optimizedIl, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(["4 5", "45"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Async iterators (Roslyn keeps them as state machines with runtime-async=on too): awaits between yields, some
    // completing at once and some suspending; a yield in nested protected blocks whose finally awaits, left early by a
    // break (DisposeAsync runs the finally blocks); an enumerator disposed before its first MoveNextAsync (no body
    // runs); a throw after a yield (that MoveNextAsync faults, the next returns false); the method's
    // [EnumeratorCancellation] token with WithCancellation's; one enumerable enumerated twice (a fresh enumerator the
    // second time); an iterator with no await; a generic method's iterator and one in a generic class (their machines are
    // generic). DesktopIteratorOutput is desktop .NET 11's, Debug and Release.
    private const string IteratorSource = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        public static class Program
        {
            private static readonly List<string> Log = new();

            private static async IAsyncEnumerable<int> Counted(int count)
            {
                for (var index = 0; index < count; index++)
                {
                    if (index % 3 == 0) await Task.Yield();
                    else await Task.CompletedTask;
                    yield return index * index;
                }
            }

            private static async IAsyncEnumerable<string> Guarded(string name)
            {
                try
                {
                    Log.Add(name + " start");
                    yield return name + "-1";
                    try
                    {
                        await Task.Yield();
                        yield return name + "-2";
                    }
                    finally
                    {
                        await Task.Yield();
                        Log.Add(name + " inner finally");
                    }
                    yield return name + "-3";
                }
                finally
                {
                    Log.Add(name + " outer finally");
                }
            }

            private static async IAsyncEnumerable<int> Failing()
            {
                yield return 1;
                await Task.Yield();
                throw new InvalidOperationException("iterator failed");
            }

            private static async IAsyncEnumerable<int> Cancellable(int count,
                [EnumeratorCancellation] CancellationToken token = default)
            {
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                    yield return index;
                }
            }

            private static async IAsyncEnumerable<T> Pairs<T>(T first, T second)
            {
                try
                {
                    yield return first;
                    await Task.Yield();
                    yield return second;
                }
                finally
                {
                    Log.Add("pairs done");
                }
            }

            private sealed class Repeater<T>(T value)
            {
                private readonly T _value = value;

                public async IAsyncEnumerable<T> Items(int count)
                {
                    for (var index = 0; index < count; index++)
                    {
                        await Task.Yield();
                        yield return _value;
                    }
                }
            }

        #pragma warning disable CS1998
            private static async IAsyncEnumerable<int> Immediate()
            {
                yield return 7;
                yield return 8;
            }
        #pragma warning restore CS1998

            public static async Task<int> Main()
            {
                long sum = 0;
                await foreach (var value in Counted(10)) sum += value;
                Console.WriteLine($"counted {sum}");

                await foreach (var value in Guarded("a"))
                {
                    Log.Add("got " + value);
                    if (value == "a-2") break;
                }
                Console.WriteLine(string.Join("|", Log));
                Log.Clear();

                var early = Guarded("b").GetAsyncEnumerator();
                await early.DisposeAsync();
                Console.WriteLine($"early [{string.Join("|", Log)}]");
                Log.Clear();

                var failing = Failing().GetAsyncEnumerator();
                Console.WriteLine($"failing {await failing.MoveNextAsync()} {failing.Current}");
                try
                {
                    await failing.MoveNextAsync();
                    Console.WriteLine("no throw");
                }
                catch (InvalidOperationException error)
                {
                    Console.WriteLine("threw " + error.Message);
                }
                Console.WriteLine($"after {await failing.MoveNextAsync()}");
                await failing.DisposeAsync();

                using (var outer = new CancellationTokenSource())
                using (var inner = new CancellationTokenSource())
                {
                    var seen = new List<int>();
                    try
                    {
                        await foreach (var value in Cancellable(10, inner.Token).WithCancellation(outer.Token))
                        {
                            seen.Add(value);
                            if (value == 2) outer.Cancel();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        seen.Add(-1);
                    }
                    Console.WriteLine("cancelled " + string.Join(",", seen));
                }

                var twice = Immediate();
                var total = 0;
                await foreach (var value in twice) total += value;
                await foreach (var value in twice) total += value;
                Console.WriteLine($"twice {total}");
                var generic = new List<string>();
                await foreach (var value in Pairs("x", "y")) generic.Add(value);
                await foreach (var value in new Repeater<int>(4).Items(3)) generic.Add(value.ToString());
                await foreach (var value in new Repeater<string>("s").Items(2)) generic.Add(value);
                Console.WriteLine($"generic {string.Join(",", generic)} [{string.Join("|", Log)}]");
                Log.Clear();
                return 0;
            }
        }
        """;

    private static readonly string[] DesktopIteratorOutput =
    [
        "counted 285",
        "a start|got a-1|got a-2|a inner finally|a outer finally",
        "early []",
        "failing True 1",
        "threw iterator failed",
        "after False",
        "cancelled 0,1,2,-1",
        "twice 30",
        "generic x,y,4,4,4,s,s [pairs done]",
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    [InlineData(FinalWasmOptimization.None, false, true)]
    [InlineData(FinalWasmOptimization.Oz, true, true)]
    public async Task AsyncIteratorsRunAsOnDesktop(FinalWasmOptimization optimization, bool optimizedIl,
        bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncIterators", IteratorSource,
            TestAssets.RepositorySdkVersion, "latest", optimizedIl, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines, iterators: true);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(DesktopIteratorOutput, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // The first pass of an exception thrown in a direct call of a method written as runtime async reaches its caller's
    // filter before the callee's finally blocks and context restore run, as desktop .NET 11's runtime async does: the
    // callee has no handler of its own there. After a suspension the exception comes from the awaited task, and a
    // method compiled as a state machine hands it to its task, so the callee's finally runs first. A direct and an
    // interface (DirectVirtual) call; and an async iterator (a state machine in both compilations), whose own catch takes
    // the exception, in a program that changes an AsyncLocal, so the caller's filter sees the context restored; and a
    // callee with no protected block of its own (a separate body, whose entry's catch restores the context). The
    // outputs are desktop .NET 11's, Debug and Release.
    private const string FilterOrderSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        public static class Program
        {
            private static readonly AsyncLocal<int> Local = new();
            private static readonly List<string> Log = new();

            private static async Task Callee(bool suspend)
            {
                Local.Value = 5;
                try
                {
                    if (suspend) await Task.Yield();
                    throw new InvalidOperationException("x");
                }
                finally
                {
                    Log.Add("finally " + Local.Value);
                }
            }

            private interface IThrower
            {
                Task Throw();
            }

            private sealed class Thrower : IThrower
            {
                public async Task Throw()
                {
                    Local.Value = 7;
                    try
                    {
                        throw new InvalidOperationException("y");
                    }
                    finally
                    {
                        Log.Add("finally " + Local.Value);
                    }
                }
            }

            private static async Task Virtual(IThrower thrower)
            {
                try
                {
                    await thrower.Throw();
                }
                catch (InvalidOperationException) when (Filter())
                {
                    Log.Add("caught " + Local.Value);
                }
            }

        #pragma warning disable CS1998
            private static async IAsyncEnumerable<int> Failing()
            {
                try
                {
                    yield return 1;
                    Local.Value = 9;
                    throw new InvalidOperationException("z");
                }
                finally
                {
                    Log.Add("finally " + Local.Value);
                }
            }
        #pragma warning restore CS1998

            // MoveNextAsync called by hand: await foreach's finally awaits DisposeAsync, which Roslyn lowers to a catch of
            // everything that would take the exception before the filter.
            private static async Task Iterate()
            {
                var enumerator = Failing().GetAsyncEnumerator();
                try
                {
                    while (await enumerator.MoveNextAsync()) Log.Add("item " + enumerator.Current);
                }
                catch (InvalidOperationException) when (Filter())
                {
                    Log.Add("caught " + Local.Value);
                }
            }

            private static bool Filter()
            {
                Log.Add("filter " + Local.Value);
                return true;
            }

            // No protected block of its own: it changes the context and throws.
            private static async Task Bare(bool suspend)
            {
                Local.Value = 3;
                if (suspend) await Task.Yield();
                throw new InvalidOperationException("w");
            }

            private static async Task BareCaller(bool suspend)
            {
                try
                {
                    await Bare(suspend);
                }
                catch (InvalidOperationException) when (Filter())
                {
                    Log.Add("caught " + Local.Value);
                }
            }

            private static async Task Caller(bool suspend)
            {
                try
                {
                    await Callee(suspend);
                }
                catch (InvalidOperationException) when (Filter())
                {
                    Log.Add("caught " + Local.Value);
                }
            }

            public static async Task Main()
            {
                await Caller(suspend: false);
                Console.WriteLine("direct: " + string.Join(" | ", Log));
                Log.Clear();
                await Caller(suspend: true);
                Console.WriteLine("suspended: " + string.Join(" | ", Log));
                Log.Clear();
                await Virtual(new Thrower());
                Console.WriteLine("virtual: " + string.Join(" | ", Log));
                Log.Clear();
                await Iterate();
                Console.WriteLine("iterator: " + string.Join(" | ", Log));
                Log.Clear();
                await BareCaller(suspend: false);
                Console.WriteLine("bare: " + string.Join(" | ", Log));
                Log.Clear();
                await BareCaller(suspend: true);
                Console.WriteLine("bare suspended: " + string.Join(" | ", Log));
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    [InlineData(FinalWasmOptimization.None, false, true)]
    [InlineData(FinalWasmOptimization.Oz, true, true)]
    public async Task AnExceptionReachesTheCallersFilterAsOnDesktop(FinalWasmOptimization optimization, bool optimizedIl,
        bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncFilterOrder", FilterOrderSource,
            TestAssets.RepositorySdkVersion, "latest", optimizedIl, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines, iterators: true);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(
            stateMachines
                ?
                [
                    "direct: finally 5 | filter 0 | caught 0",
                    "suspended: finally 5 | filter 0 | caught 0",
                    "virtual: finally 7 | filter 0 | caught 0",
                    "iterator: item 1 | finally 9 | filter 0 | caught 0",
                    "bare: filter 0 | caught 0",
                    "bare suspended: filter 0 | caught 0",
                ]
                :
                [
                    "direct: filter 5 | finally 5 | caught 0",
                    "suspended: finally 5 | filter 0 | caught 0",
                    "virtual: filter 7 | finally 7 | caught 0",
                    "iterator: item 1 | finally 9 | filter 0 | caught 0",
                    "bare: filter 3 | caught 0",
                    "bare suspended: filter 0 | caught 0",
                ],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // Which runtime-async methods can suspend, as the lowering reads it from the whole program at the presets that take
    // whole-program facts (RuntimeAsyncSuspensionFacts): a chain that cannot (an await-less leaf, a method awaiting only
    // such, recursion with nothing else awaited, a generic chain, a ValueTask chain), awaited directly and called for its
    // task; a chain whose leaf yields; mutual recursion with one member yielding; an exception through awaits that
    // complete at once; a virtual await. The output is desktop .NET 11's, Debug and Release, both compilations.
    private const string SuspensionSource = """
        using System;
        using System.Threading.Tasks;

        public interface IStep
        {
            Task<int> Run(int value);
        }

        public sealed class Doubling : IStep
        {
            public async Task<int> Run(int value) => await Plain.Leaf(value) * 2;
        }

        public static class Plain
        {
            // Cannot suspend: no await at all.
            public static async Task<int> Leaf(int value) => value + 1;

            // Cannot suspend: awaits only methods that cannot.
            public static async Task<int> Middle(int value) => await Leaf(value) + await Leaf(value + 10);

            public static async Task<int> Top(int value) => await Middle(value) * 3;

            // Cannot suspend: recursion with nothing else awaited.
            public static async Task<int> Countdown(int value) => value == 0 ? 0 : await Countdown(value - 1) + value;

            // A chain whose leaf yields: every method above it can suspend.
            public static async Task<int> Yields(int value)
            {
                await Task.Yield();
                return value + 100;
            }

            public static async Task<int> AboveYields(int value) => await Yields(value) + 1;

            // Mutual recursion, one member yielding on some calls: the whole cycle can suspend.
            public static async Task<int> Ping(int value) => value <= 0 ? 0 : await Pong(value - 1) + 1;

            public static async Task<int> Pong(int value)
            {
                if (value % 3 == 0) await Task.Yield();
                return value <= 0 ? 0 : await Ping(value - 1) + 10;
            }

            // An exception from a method that cannot suspend, through awaits that complete at once.
            public static async Task<int> Throws(int value) =>
                value < 0 ? throw new InvalidOperationException("negative " + value) : value;

            public static async Task<int> CatchesThrown(int value)
            {
                try
                {
                    return await Throws(value) + await Throws(-value);
                }
                catch (InvalidOperationException error)
                {
                    return error.Message.Length;
                }
            }

            // A virtual await of an override that cannot suspend.
            public static async Task<int> Virtual(IStep step, int value) => await step.Run(value) + 1;

            // A generic chain that cannot suspend.
            public static async Task<T> Echo<T>(T value) => value;

            public static async Task<string> Echoes(int value) => (await Echo(value)) + "/" + await Echo("s" + value);

            // A ValueTask chain that cannot suspend.
            public static async ValueTask<int> ValueLeaf(int value) => value * 7;

            public static async ValueTask<int> ValueTop(int value) => await ValueLeaf(value) + await ValueLeaf(1);
        }

        public static class Program
        {
            public static async Task<int> Main()
            {
                Console.WriteLine("top " + await Plain.Top(4));
                Console.WriteLine("top normal " + Plain.Top(5).Result + " " + Plain.Top(6).IsCompleted);
                Console.WriteLine("countdown " + await Plain.Countdown(30));
                Console.WriteLine("above yields " + await Plain.AboveYields(1));
                var pending = Plain.AboveYields(2);
                Console.WriteLine("above yields pending " + pending.IsCompleted + " " + await pending);
                Console.WriteLine("ping " + await Plain.Ping(10));
                Console.WriteLine("caught " + await Plain.CatchesThrown(5));
                Console.WriteLine("virtual " + await Plain.Virtual(new Doubling(), 3));
                Console.WriteLine("echoes " + await Plain.Echoes(9));
                Console.WriteLine("value " + await Plain.ValueTop(2));
                return 0;
            }
        }
        """;

    private static readonly string[] DesktopSuspensionOutput =
    [
        "top 60",
        "top normal 66 True",
        "countdown 465",
        "above yields 102",
        "above yields pending False 103",
        "ping 55",
        "caught 11",
        "virtual 9",
        "echoes 9/s9",
        "value 21",
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.O3, true)]
    [InlineData(FinalWasmOptimization.None, false, true)]
    [InlineData(FinalWasmOptimization.Oz, true, true)]
    public async Task MethodsThatCannotSuspendRunAsOnDesktop(FinalWasmOptimization optimization, bool optimizedIl,
        bool stateMachines = false)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("RuntimeAsyncSuspension", SuspensionSource,
            TestAssets.RepositorySdkVersion, "latest", optimizedIl, features: Features(stateMachines));
        var component = await Package(assets, assembly, optimization, stateMachines,
            checkProgram: optimization == FinalWasmOptimization.Oz ? AssertSuspensionFacts : null);

        var (exitCode, output) = await Run(assets.Root, component);

        Assert.True(exitCode == 0, output);
        Assert.Equal(DesktopSuspensionOutput, output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // At a whole-program preset, a method that cannot suspend is never resumed (no TakeResumingFrame), and one that can is.
    // Oz keeps TakeResumingFrame a call (O3 inlines it), and the lowering, which decides it, is the same at both.
    private static void AssertSuspensionFacts(ReachableProgram program)
    {
        string[] cannot = ["Leaf", "Middle", "Top", "Countdown", "Throws", "CatchesThrown", "Echoes", "ValueLeaf", "ValueTop"];
        string[] can = ["Yields", "AboveYields", "Ping", "Pong", "Virtual"];
        var resumed = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var body in program.Methods.Values.Concat(program.ConstructedMethods.Values))
        {
            if (body.Method.DeclaringType.FullName != "Plain") continue;
            var takesFrame = body.Body.Instructions.Any(instruction => instruction.Operand is CilOperand.MethodInstance
            {
                Value.Definition.Name: "TakeResumingFrame",
            });
            resumed[body.Method.Definition.Name] = resumed.GetValueOrDefault(body.Method.Definition.Name) | takesFrame;
        }
        Assert.Contains("Countdown", resumed.Keys);
        var facts = string.Join(", ", resumed.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.False(cannot.Any(name => resumed.GetValueOrDefault(name)), facts);
        Assert.False(can.Any(name => resumed.TryGetValue(name, out var takes) && !takes), facts);
    }

    private static IReadOnlyList<string> Features(bool stateMachines) => stateMachines ? [] : ["runtime-async=on"];

    private static async Task<string> Package(TestAssets assets, string assembly, FinalWasmOptimization optimization,
        bool stateMachines = false, bool iterators = false, Action<ReachableProgram>? checkProgram = null)
    {
        var token = MainToken(assembly);
        var compiled = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "Program", "Main", [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable,
            Optimization: optimization));
        if (stateMachines) AssertStateMachinesRewritten(compiled.Program);
        AssertIteratorsRewritten(compiled.Program, iterators);
        checkProgram?.Invoke(compiled.Program);
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "runtime-async.component.wasm");
        File.WriteAllBytes(application, compiled.ApplicationModule);
        RuntimeLayoutFiles.Write(layout, compiled);
        await Tool("bash", assets.Root, Path.Combine(assets.Root, "eng", "build-netwasm-runtime.sh"),
            "--runtime-layout", layout, "--output", runtime, "--target", "wasm32", "--configuration", "release");
        var toolchainWit = Path.Combine(assets.Root, "src", "NetWasm.Toolchain", "wit");
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var world = services.GetRequiredService<IWitDocumentReader>().Read(toolchainWit)
            .SelectWorld("netwasm:command/command@1.0.0");
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, toolchainWit, services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world), component,
            ComponentTarget.Wasm32Wasi02, runtime,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.ExitCode,
                ManagedExecutableCompletionShape.Asynchronous),
            optimization));
        return component;
    }

    // Every reached method that Roslyn compiled to a state machine was rewritten: none of their machines' MoveNext is
    // reached, which a kickoff that kept its machine (awaiting the task it starts) would reach.
    private static void AssertStateMachinesRewritten(ReachableProgram program)
    {
        var reached = program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .Select(body => body.Method)
            .ToArray();
        var machines = reached.Where(method => method.Definition.StateMachineMoveNext is not null)
            .Select(method => method.Definition.StateMachineMoveNext!.Value)
            .ToHashSet();
        Assert.NotEmpty(machines);
        Assert.Empty(reached.Where(method => machines.Contains(method.Definition.Key)).Select(method => method.CanonicalName));
    }

    // An async iterator machine's MoveNextAsync and DisposeAsync are rewritten from its MoveNext, which only their
    // fallback (the builder running MoveNext) reaches.
    private static void AssertIteratorsRewritten(ReachableProgram program, bool expected)
    {
        var reached = program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .Select(body => body.Method)
            .ToArray();
        var moveNexts = reached.Where(method => method.Definition.AsyncIteratorMoveNext is not null)
            .Select(method => method.Definition.AsyncIteratorMoveNext!.Value)
            .ToHashSet();
        if (expected) Assert.NotEmpty(moveNexts);
        Assert.Empty(reached.Where(method => moveNexts.Contains(method.Definition.Key)).Select(method => method.CanonicalName));
    }

    private static int MainToken(string assembly)
    {
        using var portableExecutable = new PEReader(File.OpenRead(assembly));
        var metadata = portableExecutable.GetMetadataReader();
        return MetadataTokens.GetToken(metadata.MethodDefinitions.Single(handle =>
            metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Main"));
    }

    private static async Task<(int ExitCode, string Output)> Run(string workingDirectory, string component)
    {
        var start = new ProcessStartInfo("wasmtime")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "run", component },
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Wasmtime did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await ProcessExits.WithinLimit(process, "wasmtime running " + component);
        return (process.ExitCode, (await output) + (await error));
    }

    private static async Task Tool(string executable, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
