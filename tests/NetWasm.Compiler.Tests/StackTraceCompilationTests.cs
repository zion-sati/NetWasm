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
                        error.StackTrace.Contains("method#") ? 0 : -30;
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
    public void InstrumentedBuildFallsBackToMethodIdsWithoutSidecar(WasmTarget target)
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
}
