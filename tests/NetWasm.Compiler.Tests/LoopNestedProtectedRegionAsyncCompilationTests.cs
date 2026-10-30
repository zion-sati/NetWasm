using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Async methods whose loop holds a try/catch around an await and a nested try/finally. The
// await's resume path enters the loop from the method's state dispatch, so the protected code
// before the nested try is an irreducible dispatcher whose exits are the await's suspension and
// the fallthrough into the nested try. Upstream ManagedWebSocket.CloseAsyncPrivate has this shape.
// Each case runs through the reactor and checks the result and how often the finally and catch
// ran, so code that runs on the wrong exit fails even when nothing suspends.
public sealed class LoopNestedProtectedRegionAsyncCompilationTests
{
    public static TheoryData<string, bool, WasmTarget> Cases()
    {
        var cases = new TheoryData<string, bool, WasmTarget>();
        foreach (var shape in Shapes.Keys)
        {
            foreach (var optimize in new[] { false, true })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    cases.Add(shape, optimize, target);
                }
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AsyncLoopsWithNestedProtectedRegionsCompleteThroughTheReactor(
        string shape,
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = Prefix + Shapes[shape] + Suffix;
        var name = $"LoopNestedProtected{shape}{(optimize ? "Release" : "Debug")}{target}";
        var assembly = optimize
            ? assets.CompileOptimizedSource(name, source)
            : assets.CompileSource(name, source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "LoopNestedProtected.EntryPoint",
            "Run",
            [
                new("run", "LoopNestedProtected.EntryPoint", "Run"),
                new("observe", "LoopNestedProtected.EntryPoint", "Observe"),
            ],
            Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd, drainReactor: true, observeExportName: "observe"));
    }

    private const string Prefix = """
        using System;
        using System.Threading.Tasks;

        namespace LoopNestedProtected;

        public static class EntryPoint
        {
            private static Task<int>? s_pending;
            private static TaskCompletionSource? s_gate;
            private static int s_finally;
            private static int s_caught;

            public static int Run(int input)
            {
                s_pending = Body();
                return input + 1;
            }

            public static int Observe() =>
                s_pending is not { IsCompleted: true } pending ? 98
                : pending.IsFaulted || pending.IsCanceled ? 99
                : pending.Result;

            // Completes on a later reactor turn, so awaiting it suspends.
            private static async ValueTask<int> Later()
            {
                await s_gate!.Task.ConfigureAwait(false);
                return 1;
            }

            private static ValueTask<int> Gated()
            {
                s_gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return Later();
            }

        """;

    private const string Suffix = """

        }
        """;

    private static readonly Dictionary<string, string> Shapes = new()
    {
        ["FilteredRethrowSuspends"] = """
                private static async Task<int> Body()
                {
                    var received = 0;
                    while (received == 0)
                    {
                        ValueTask<int> pending = default;
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                pending = Gated();
                            }
                            finally
                            {
                                s_finally++;
                                s_gate!.TrySetResult();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }

                        received = await pending.ConfigureAwait(false);
                    }
                    return received == 1 && s_finally == 1 ? 42 : 1;
                }
            """,
        ["CatchAllRethrowSuspends"] = """
                private static async Task<int> Body()
                {
                    var received = 0;
                    while (received == 0)
                    {
                        ValueTask<int> pending = default;
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                pending = Gated();
                            }
                            finally
                            {
                                s_finally++;
                                s_gate!.TrySetResult();
                            }
                        }
                        catch
                        {
                            throw;
                        }

                        received = await pending.ConfigureAwait(false);
                    }
                    return received == 1 && s_finally == 1 ? 42 : 2;
                }
            """,
        ["FilteredSwallowSuspends"] = """
                private static async Task<int> Body()
                {
                    var received = 0;
                    while (received == 0)
                    {
                        ValueTask<int> pending = default;
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                pending = Gated();
                            }
                            finally
                            {
                                s_finally++;
                                s_gate!.TrySetResult();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            s_caught++;
                        }

                        received = await pending.ConfigureAwait(false);
                    }
                    return received == 1 && s_finally == 1 && s_caught == 0 ? 42 : 3;
                }
            """,
        ["CompletesWithoutSuspending"] = """
                private static async Task<int> Body()
                {
                    var received = 0;
                    while (received == 0)
                    {
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                received = 1;
                            }
                            finally
                            {
                                s_finally++;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                    }
                    return received == 1 && s_finally == 1 ? 42 : 4;
                }
            """,
        ["AwaitsAfterTheNestedFinally"] = """
                private static async Task<int> Body()
                {
                    var received = 0;
                    while (received == 0)
                    {
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                s_finally += 10;
                            }
                            finally
                            {
                                s_finally++;
                            }
                            var pending = Gated();
                            s_gate!.TrySetResult();
                            received = await pending.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                    }
                    return received == 1 && s_finally == 11 ? 42 : 5;
                }
            """,
        ["NestedLoops"] = """
                private static async Task<int> Body()
                {
                    var total = 0;
                    for (var outer = 0; outer < 2; outer++)
                    {
                        var received = 0;
                        while (received == 0)
                        {
                            ValueTask<int> pending = default;
                            try
                            {
                                await Task.CompletedTask;
                                try
                                {
                                    pending = Gated();
                                }
                                finally
                                {
                                    s_finally++;
                                    s_gate!.TrySetResult();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }

                            received = await pending.ConfigureAwait(false);
                        }
                        total += received;
                    }
                    return total == 2 && s_finally == 2 ? 42 : 6;
                }
            """,
        ["ValueTaskMethod"] = """
                private static Task<int> Body() => BodyCore().AsTask();

                private static async ValueTask<int> BodyCore()
                {
                    var received = 0;
                    while (received == 0)
                    {
                        ValueTask<int> pending = default;
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                pending = Gated();
                            }
                            finally
                            {
                                s_finally++;
                                s_gate!.TrySetResult();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }

                        received = await pending.ConfigureAwait(false);
                    }
                    return received == 1 && s_finally == 1 ? 42 : 7;
                }
            """,
        ["ThrowsThroughTheNestedFinally"] = """
                private static async Task<int> Body()
                {
                    var received = 0;
                    var attempt = 0;
                    while (received == 0)
                    {
                        ValueTask<int> pending = default;
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                if (attempt++ == 0)
                                {
                                    throw new OperationCanceledException();
                                }
                                pending = Gated();
                            }
                            finally
                            {
                                s_finally++;
                                s_gate?.TrySetResult();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            s_caught++;
                            continue;
                        }

                        received = await pending.ConfigureAwait(false);
                    }
                    return received == 1 && s_finally == 2 && s_caught == 1 ? 42 : 8;
                }
            """,
        ["SuspendsOnEveryIteration"] = """
                private static async Task<int> Body()
                {
                    var total = 0;
                    var received = 0;
                    while (total < 3)
                    {
                        ValueTask<int> pending = default;
                        try
                        {
                            await Task.CompletedTask;
                            try
                            {
                                pending = Gated();
                            }
                            finally
                            {
                                s_finally++;
                                s_gate!.TrySetResult();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }

                        received = await pending.ConfigureAwait(false);
                        total += received;
                    }
                    return total == 3 && s_finally == 3 ? 42 : 9;
                }
            """,
    };
}
