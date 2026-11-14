using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// NW1014: a loop body's catch handler holds a try/finally whose try leaves the iteration
// (`continue`, or `break`), inside foreach's own try/finally around the loop. The leave crosses
// the inner finally and the handler on its way to the loop's continue target, so it needs a
// continuation in each region it exits. The structurer stopped with "Executing leave block N
// (Owner) has no active structured continuation"; the same body in a for loop compiled. The
// async shapes follow AWSSDK.S3's DefaultS3ExpressCredentialProvider.RefreshCredentials, which
// hit it in the representative package corpus. Each shape's result was computed on desktop
// .NET 10 in Debug and Release, which agree.
public sealed class LeaveThroughHandlerFinallyCompilationTests
{
    public static TheoryData<bool, WasmTarget> Cases() => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void LeavesThroughAFinallyInsideALoopsCatchHandlerMatchDesktop(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var name = $"LeaveThroughHandlerFinally{(optimize ? "Release" : "Debug")}{target}";
        var (source, entryType) = AsyncObservationEntry.Append(
            Source, "LeaveThroughHandlerFinally.EntryPoint", "Run", "Observe");
        var assembly = optimize ? assets.CompileOptimizedSource(name, source) : assets.CompileSource(name, source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], entryType,
            AsyncObservationEntry.MethodName,
            [],
            Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        var observed = CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd, callbackEntry: true, inputArgument: true);

        // 42 when every shape matches; otherwise 1000 plus a bit per mismatching shape.
        Assert.Equal(42, observed);
    }

    private const string Source = """
        #pragma warning disable CS0162
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace LeaveThroughHandlerFinally;

        public sealed class MissingException : Exception { }

        public static class Shapes
        {
            private static readonly int[] Keys = [1, 2, 4, 5, 8, 3];
            private static int s_added;
            private static int s_evicted;

            private static int Create(int key) => key % 4 == 0 ? throw new MissingException() : key;

            private static int Result() => s_added * 1000 + s_evicted;

            // The brief's repro: foreach's own try/finally around the loop, a catch handler whose
            // try/finally continues the loop.
            public static int ForeachContinue(List<int> keys)
            {
                s_added = s_evicted = 0;
                foreach (var key in keys)
                {
                    try { s_added += Create(key); }
                    catch (MissingException)
                    {
                        try { s_evicted++; continue; }
                        finally { s_evicted += 2; }
                    }
                    s_added += 100;
                }
                return Result();
            }

            public static int ForContinue(List<int> keys)
            {
                s_added = s_evicted = 0;
                for (var index = 0; index < keys.Count; index++)
                {
                    try { s_added += Create(keys[index]); }
                    catch (MissingException)
                    {
                        try { s_evicted++; continue; }
                        finally { s_evicted += 2; }
                    }
                    s_added += 100;
                }
                return Result();
            }

            public static int ForeachBreak(List<int> keys)
            {
                s_added = s_evicted = 0;
                foreach (var key in keys)
                {
                    try { s_added += Create(key); }
                    catch (MissingException)
                    {
                        try { s_evicted++; if (s_evicted > 3) break; }
                        finally { s_evicted += 2; }
                    }
                    s_added += 100;
                }
                return Result();
            }

            public static int ForeachContinueTwoFinally(List<int> keys)
            {
                s_added = s_evicted = 0;
                foreach (var key in keys)
                {
                    try { s_added += Create(key); }
                    catch (MissingException)
                    {
                        try
                        {
                            try { s_evicted++; continue; }
                            finally { s_evicted += 2; }
                        }
                        finally { s_evicted += 10; }
                    }
                    s_added += 100;
                }
                return Result();
            }

            public static int ArrayForeachContinue()
            {
                s_added = s_evicted = 0;
                foreach (var key in Keys)
                {
                    try { s_added += Create(key); }
                    catch (MissingException)
                    {
                        try { s_evicted++; continue; }
                        finally { s_evicted += 2; }
                    }
                    s_added += 100;
                }
                return Result();
            }

            public static async Task<int> AsyncForeachContinue(List<int> keys)
            {
                int added = 0, evicted = 0;
                try
                {
                    foreach (var key in keys)
                    {
                        try { await Task.Yield(); added += Create(key); }
                        catch (MissingException)
                        {
                            try { evicted++; continue; }
                            finally { evicted += 2; }
                        }
                        added += 100;
                    }
                }
                catch (InvalidOperationException) { return -1; }
                return added * 1000 + evicted;
            }

            public static async Task<int> AsyncForeachContinueInsideCatch(List<int> keys)
            {
                int added = 0, evicted = 0;
                try
                {
                    await Task.Yield();
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    foreach (var key in keys)
                    {
                        try { await Task.Yield(); added += Create(key); }
                        catch (MissingException)
                        {
                            try { evicted++; continue; }
                            finally { evicted += 2; }
                        }
                        added += 100;
                    }
                }
                return added * 1000 + evicted;
            }

            public static async Task<int> AsyncForeachAwaitInHandlerContinue(List<int> keys)
            {
                int added = 0, evicted = 0;
                foreach (var key in keys)
                {
                    try { await Task.Yield(); added += Create(key); }
                    catch (MissingException)
                    {
                        try { await Task.Yield(); evicted++; continue; }
                        finally { evicted += 2; }
                    }
                    added += 100;
                }
                return added * 1000 + evicted;
            }

            public static async Task<int> Call(int index)
            {
                List<int> keys = [.. Keys];
                return index switch
                {
                    0 => ForeachContinue(keys),
                    1 => ForContinue(keys),
                    2 => ForeachBreak(keys),
                    3 => ForeachContinueTwoFinally(keys),
                    4 => ArrayForeachContinue(),
                    5 => await AsyncForeachContinue(keys),
                    6 => await AsyncForeachContinueInsideCatch(keys),
                    7 => await AsyncForeachAwaitInHandlerContinue(keys),
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                };
            }

            public const int Count = 8;
        }

        public static class EntryPoint
        {
            private static readonly int[] Expected = [411006, 411006, 408006, 411026, 411006, 411006, 411006, 411006];
            private static Task<int>? s_pending;

            public static int Run(int input)
            {
                s_pending = Check();
                return input + 1;
            }

            public static int Observe() =>
                s_pending is not { IsCompleted: true } pending ? 98
                : pending.IsFaulted || pending.IsCanceled ? 99
                : pending.Result;

            private static async Task<int> Check()
            {
                var mask = 0;
                for (var index = 0; index < Shapes.Count; index++)
                {
                    if (await Shapes.Call(index) != Expected[index]) mask |= 1 << index;
                }
                return mask == 0 ? 42 : 1000 + mask;
            }
        }
        """;
}
