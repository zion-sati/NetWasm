using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Tests;

public sealed class StackTraceOptInCompilationTests
{
    // Run(41) checks StackTrace and StackFrame against the managed stack trace that the opt-in
    // instrumentation captures; the same checks hold on desktop .NET. Run(40) checks that every
    // capturing constructor throws PlatformNotSupportedException without the instrumentation.
    internal const string Source = """
        using System;
        using System.Diagnostics;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
        #if !DESKTOP_ORACLE
                if (input == 40)
                {
                    return Uninstrumented() is int failure ? failure : input + 2;
                }
        #endif
                Exception error = Capture();
                string? raw = error.StackTrace;
                if (raw is null) return 1;

                // An exception's frames are the ones Exception.StackTrace reports.
                var trace = new StackTrace(error, true);
                if (trace.FrameCount != raw.Split('\n').Length) return 2;
                if (trace.ToString() != raw + Environment.NewLine) return 3;
                StackFrame[] frames = trace.GetFrames();
                if (frames.Length != trace.FrameCount || !ReferenceEquals(frames[0], trace.GetFrame(0))) return 4;
                if (trace.GetFrame(trace.FrameCount) is not null || trace.GetFrame(-1) is not null) return 5;
                StackFrame leaf = frames[0];
                if (leaf.GetFileName()?.EndsWith("TraceFixture.cs", StringComparison.Ordinal) != true ||
                    leaf.GetFileLineNumber() != 101 || !leaf.HasSource()) return 6;
                if (!leaf.ToString().Contains("Leaf") ||
                    !leaf.ToString().Contains(" in file:line:column ", StringComparison.Ordinal)) return 7;
        #if !DESKTOP_ORACLE
                if (leaf.GetILOffset() != StackFrame.OFFSET_UNKNOWN ||
                    leaf.GetNativeOffset() != StackFrame.OFFSET_UNKNOWN ||
                    leaf.GetFileColumnNumber() != 0 || leaf.HasILOffset()) return 8;
                if (!Throws<PlatformNotSupportedException>(() => leaf.GetMethod())) return 9;
        #endif

                // Skipped frames, file information and empty traces follow upstream.
                var skipped = new StackTrace(error, 1, true);
                if (skipped.FrameCount != trace.FrameCount - 1 ||
                    skipped.ToString() != raw.Substring(raw.IndexOf('\n') + 1) + Environment.NewLine) return 10;
                var withoutFiles = new StackTrace(error);
                if (withoutFiles.GetFrame(0)!.GetFileName() is not null ||
                    withoutFiles.GetFrame(0)!.GetFileLineNumber() != 0) return 11;
                var beyond = new StackTrace(error, 1000);
                if (beyond.FrameCount != 0 || beyond.ToString() != Environment.NewLine) return 12;
                if (new StackTrace(new Exception()).FrameCount != 0) return 13;
                if (new StackTrace(CaptureOverridden()).ToString().Contains("overridden")) return 14;

                // The current thread's frames start at the constructor's caller.
                StackTrace here = Here();
                if (!here.GetFrame(0)!.ToString().Contains("Here") ||
                    !here.GetFrame(1)!.ToString().Contains("Run")) return 15;
                if (!SkipOne().GetFrame(0)!.ToString().Contains("Run")) return 16;
                StackFrame located = Located().GetFrame(0)!;
                if (located.GetFileLineNumber() != 303 ||
                    located.GetFileName()?.EndsWith("TraceFixture.cs", StringComparison.Ordinal) != true) return 17;
                if (!CurrentFrame().ToString().Contains("CurrentFrame") ||
                    !CallerFrame().ToString().Contains("Run")) return 18;
                var given = new StackFrame("given.cs", 7);
                if (given.GetFileName() != "given.cs" || given.GetFileLineNumber() != 7) return 19;
                if (new StackTrace(given).FrameCount != 1) return 20;
                return input + 1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Exception Capture()
            {
                try { Leaf(); }
                catch (Exception error) { return error; }
                return null!;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Leaf()
            {
        #line 101 "Safe/TraceFixture.cs"
                throw new InvalidOperationException("leaf");
        #line default
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Exception CaptureOverridden()
            {
                try { throw new OverriddenException(); }
                catch (Exception error) { return error; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static StackTrace Here() => new StackTrace(true);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static StackTrace SkipOne() => new StackTrace(1, true);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static StackTrace Located()
            {
        #line 303 "Safe/TraceFixture.cs"
                return new StackTrace(true);
        #line default
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static StackFrame CurrentFrame() => new StackFrame(true);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static StackFrame CallerFrame() => new StackFrame(1, true);

            private static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; }
                catch (T) { return true; }
            }

        #if !DESKTOP_ORACLE
            private static int? Uninstrumented()
            {
                Exception error = Capture();
                if (error.StackTrace is not null) return 30;
                if (!Throws<PlatformNotSupportedException>(() => new StackTrace())) return 31;
                if (!Throws<PlatformNotSupportedException>(() => new StackTrace(1, true))) return 32;
                if (!Throws<PlatformNotSupportedException>(() => new StackTrace(error, true))) return 33;
                if (!Throws<PlatformNotSupportedException>(() => new StackTrace(new Exception()))) return 34;
                if (!Throws<PlatformNotSupportedException>(() => new StackFrame())) return 35;
                if (!Throws<PlatformNotSupportedException>(() => new StackFrame("given.cs", 7))) return 36;
                if (!Throws<ArgumentNullException>(() => new StackTrace((Exception)null!))) return 37;
                return null;
            }
        #endif

            private sealed class OverriddenException : Exception
            {
                public override string StackTrace => "overridden";
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InstrumentedBuildsReadFramesFromTheManagedStackTrace(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"StackTraceOptIn{optimize}{target}", Source, "EntryPoint", optimize, target, 41, [])
        {
            EmitPortablePdb = true,
            EmitStackTrace = true,
        });

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void UninstrumentedBuildsRejectStackCapture(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"StackTraceOptOut{optimize}{target}", Source, "EntryPoint", optimize, target, 40, []));

        Assert.Equal(42, result);
    }
}
