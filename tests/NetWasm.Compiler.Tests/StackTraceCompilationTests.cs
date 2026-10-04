using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Tests;

public sealed class StackTraceCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            private delegate void Thrower();

            public static int Run(int enabled)
            {
                if (enabled == 0)
                {
                    try { ThrowLeaf(); }
                    catch (Exception error)
                    {
                        return error.StackTrace is null ? 0 : -1;
                    }
                }

                if (enabled == 2)
                {
                    var error = Capture(ThrowImplicitly);
                    return error is null
                        ? -20
                        : HasStackTrace(error) ? 0 : -21;
                }

                if (enabled == 3)
                {
                    var error = Capture(ThrowLeaf);
                    return error.StackTrace is not null &&
                        error.StackTrace.Contains("ThrowLeaf") &&
                        !error.StackTrace.Contains("method#") ? 0 : -30;
                }

                var nested = Capture(() => Generic<int>.Throw());
                if (nested.StackTrace is null ||
                    !nested.StackTrace.Contains("ThrowLeaf") ||
                    CountFrames(nested.StackTrace) < 4)
                {
                    return -2;
                }

                var constructed = Capture(() => new ThrowingType());
                if (constructed.StackTrace is null ||
                    CountFrames(constructed.StackTrace) < 3)
                {
                    return -3;
                }

                var implicitFailure = Capture(ThrowImplicitly);
                if (implicitFailure is null)
                {
                    return -40;
                }
                if (implicitFailure.StackTrace is null)
                {
                    return -4;
                }

                var preserved = CaptureRethrow();
                var recaptured = CaptureThrowException();
                if (preserved.StackTrace is null || recaptured.StackTrace is null ||
                    CountFrames(preserved.StackTrace) <= CountFrames(recaptured.StackTrace))
                {
                    return -5;
                }

                var finallyRan = false;
                try
                {
                    ThrowLeaf();
                }
                catch (Exception error) when (error.StackTrace is not null)
                {
                }
                finally
                {
                    finallyRan = true;
                }
                return finallyRan ? 0 : -6;
            }

            private static Exception Capture(Thrower thrower)
            {
                try { thrower(); }
                catch (Exception error) { return error; }
                throw new Exception("unreachable");
            }

            private static Exception CaptureRethrow()
            {
                try { ThrowLeaf(); }
                catch
                {
                    try { throw; }
                    catch (Exception error) { return error; }
                }
                throw new Exception("unreachable");
            }

            private static Exception CaptureThrowException()
            {
                try { ThrowLeaf(); }
                catch (Exception error)
                {
                    try { throw error; }
                    catch (Exception recaptured) { return recaptured; }
                }
                throw new Exception("unreachable");
            }

            private static int CountFrames(string trace)
            {
                var count = 1;
                for (var index = 0; index < trace.Length; index++)
                {
                    if (trace[index] == '\n') { count++; }
                }
                return count;
            }

            private static bool HasStackTrace(Exception error) =>
                error is not null && error.StackTrace is not null;

            private static void ThrowLeaf() => throw new Exception("leaf");

            private static void ThrowImplicitly()
            {
                int[] values = null!;
                _ = values.Length;
            }

            private sealed class ThrowingType
            {
                public ThrowingType() => ThrowLeaf();
            }

            private static class Generic<T>
            {
                public static void Throw() => ThrowLeaf();
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InstrumentedBuildCapturesManagedFrames(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "StackTraceEnabled",
            Source,
            "EntryPoint",
            optimize,
            target,
            1,
            [])
        {
            EmitStackTrace = true,
        });

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DisabledBuildLeavesStackTraceNull(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "StackTraceDisabled",
            Source,
            "EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, result);
    }

    [Fact]
    public void InstrumentedBuildCapturesImplicitExceptionFrame()
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "StackTraceImplicit",
            Source,
            "EntryPoint",
            false,
            WasmTarget.Wasm32,
            2,
            [])
        {
            EmitStackTrace = true,
        });

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InstrumentedBuildUsesEmbeddedSymbolsWithoutSidecar(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "StackTraceFallback",
            Source,
            "EntryPoint",
            false,
            target,
            3,
            [])
        {
            EmitStackTrace = true,
            LoadStackTraceSymbols = false,
        });

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    [Trait("Issue", "64")]
    public void PortablePdbDistinguishesLocationsWithinOneMethodWithoutHostSidecar(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        const string source = """
            using System;

            public static class EntryPoint
            {
                public static int Run(int location)
                {
                    try { ThrowAt(location); }
                    catch (Exception error)
                    {
                        var expected = location == 1 ? 101 : 202;
                        return error.StackTrace is not null &&
                            error.StackTrace.Contains("TraceFixture.cs:line " + expected)
                            ? 0
                            : -1;
                    }
                    return -2;
                }

                private static void ThrowAt(int location)
                {
                    if (location == 1)
                    {
            #line 101 "Safe/TraceFixture.cs"
                        throw new InvalidOperationException("first");
            #line default
                    }
            #line 202 "Safe/TraceFixture.cs"
                    throw new InvalidOperationException("second");
            #line default
                }
            }
            """;

        foreach (var location in new[] { 1, 2 })
        {
            var result = executor.Execute(new CompilationScenario(
                $"SourceLine{location}{optimize}{target}",
                source,
                "EntryPoint",
                optimize,
                target,
                location,
                [])
            {
                EmitPortablePdb = true,
                EmitStackTrace = true,
                LoadStackTraceSymbols = false,
            });

            Assert.Equal(0, result);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    [Trait("Issue", "64")]
    public void AsyncTaskDispatchPreservesOriginalSourceLocation(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        const string source = """
            using System;
            using System.Threading.Tasks;
            using System.Threading.Tasks.Sources;

            public sealed class Source : IValueTaskSource<int>
            {
                private ManualResetValueTaskSourceCore<int> _core;
                public ValueTask<int> Task => new(this, _core.Version);
                public void Complete(int value) => _core.SetResult(value);
                public int GetResult(short token) => _core.GetResult(token);
                public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
                public void OnCompleted(Action<object?> continuation, object? state,
                    short token, ValueTaskSourceOnCompletedFlags flags) =>
                    _core.OnCompleted(continuation, state, token, flags);
            }

            public static class EntryPoint
            {
                private static Task<int>? _pending;

                public static int Run(int input)
                {
                    var source = new Source();
                    _pending = FailAfter<int>(source.Task);
                    source.Complete(input);
                    return 0;
                }

                public static int Observe()
                {
                    try { return _pending!.Result; }
                    catch (InvalidOperationException error)
                    {
                        return error.StackTrace is not null &&
                            error.StackTrace.Contains("AsyncTrace.cs:line 303")
                            ? 0
                            : -1;
                    }
                }

                private static async Task<int> FailAfter<T>(ValueTask<int> gate)
                {
                    await gate;
            #line 303 "Safe/AsyncTrace.cs"
                    throw new InvalidOperationException("async failure");
            #line default
                }
            }
            """;

        var result = executor.Execute(new CompilationScenario(
            $"AsyncSourceLine{target}",
            source,
            "EntryPoint",
            false,
            target,
            1,
            [])
        {
            EmitPortablePdb = true,
            EmitStackTrace = true,
            LoadStackTraceSymbols = false,
            DrainReactor = true,
            ObserveExportName = "observe",
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/async-platform",
            Exports =
            [
                new RequestedExport("run", "EntryPoint", "Run"),
                new RequestedExport("observe", "EntryPoint", "Observe"),
            ],
        });

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Issue", "64")]
    public void PreservedDispatchCapturesFreshException(int mode)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        const string source = """
            using System;
            using System.Runtime.ExceptionServices;
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                public static int Run(int mode)
                {
                    try
                    {
                        var error = new InvalidOperationException("fresh");
                        if (mode == 1)
                        {
                            ExceptionDispatchInfo.Throw(error);
                        }
                        return Task.FromException<int>(error).Result;
                    }
                    catch (InvalidOperationException error)
                    {
                        return error.StackTrace is not null &&
                            error.StackTrace.Contains("EntryPoint::Run") ? 0 : -1;
                    }
                    return -2;
                }
            }
            """;

        var result = executor.Execute(new CompilationScenario(
            $"FreshPreservedDispatch{mode}",
            source,
            "EntryPoint",
            false,
            WasmTarget.Wasm32,
            mode,
            [])
        {
            EmitStackTrace = true,
            LoadStackTraceSymbols = false,
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/async-platform",
            Exports =
            [
                new RequestedExport("run", "EntryPoint", "Run"),
            ],
        });

        Assert.Equal(0, result);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void ConstructedGenericMethodUsesDefinitionSourceLocations()
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        const string source = """
            using System;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    try { Generic<int>.Throw(value); }
                    catch (InvalidOperationException error)
                    {
                        return error.StackTrace is not null &&
                            error.StackTrace.Contains("GenericTrace.cs:line 77") ? 0 : -1;
                    }
                    return -2;
                }

                private static class Generic<T>
                {
                    public static void Throw(T value)
                    {
            #line 77 "Safe/GenericTrace.cs"
                        throw new InvalidOperationException(value!.ToString());
            #line default
                    }
                }
            }
            """;

        var result = executor.Execute(new CompilationScenario(
            "ConstructedGenericSourceLine",
            source,
            "EntryPoint",
            false,
            WasmTarget.Wasm32,
            1,
            [])
        {
            EmitPortablePdb = true,
            EmitStackTrace = true,
            LoadStackTraceSymbols = false,
        });

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    [Trait("Issue", "64")]
    public void TerminalDiagnosticsCarryCapturedSourceLocation(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var error = Assert.ThrowsAny<Exception>(() => executor.Execute(
            new CompilationScenario(
                $"TerminalSourceLine{target}",
                """
                using System;

                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                #line 404 "Safe/TerminalTrace.cs"
                        throw new FormatException("terminal trace");
                #line default
                    }
                }
                """,
                "EntryPoint",
                false,
                target,
                0,
                [])
            {
                EmitPortablePdb = true,
                EmitStackTrace = true,
                LoadStackTraceSymbols = false,
            }));

        Assert.Contains(
            "EntryPoint::Run in Safe/TerminalTrace.cs:line 404",
            error.Message,
            StringComparison.Ordinal);
    }
}
