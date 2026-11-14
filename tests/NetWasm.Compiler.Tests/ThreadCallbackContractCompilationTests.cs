using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ThreadCallbackContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ThreadCallbacksAndExceptionIdentityWork(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ThreadCallbackFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ThreadCallbackFixture.EntryPoint",
            "Run", [new("run", "ThreadCallbackFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Runtime.Serialization;
        #pragma warning disable SYSLIB0050, SYSLIB0051
        namespace ThreadCallbackFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                try { new SerializedInterrupted(); return 30; } catch (PlatformNotSupportedException) { }
                try { new SerializedState(); return 31; } catch (PlatformNotSupportedException) { }
                return input + 1;
            }
            public static int Portable(int input)
            {
                var marker = new object(); object? observed = null; var calls = 0;
                WaitCallback callback = state => { observed = state; calls++; };
                callback(marker); if (!ReferenceEquals(observed, marker) || calls != 1) return 1;
                callback(null); if (observed != null || calls != 2) return 2;
                var timeout = false;
                WaitOrTimerCallback timed = (state, timedOut) => { observed = state; timeout = timedOut; };
                timed(marker, true); if (!ReferenceEquals(observed, marker) || !timeout) return 3;
                timed(null, false); if (observed != null || timeout) return 4;
                var error = new Exception("inner"); var args = new ThreadExceptionEventArgs(error);
                ThreadExceptionEventHandler handler = (sender, value) => { observed = sender; if (ReferenceEquals(value.Exception, error)) calls++; };
                handler(marker, args); if (!ReferenceEquals(observed, marker) || calls != 3 || !ReferenceEquals(args.Exception, error)) return 5;
                if (new ThreadExceptionEventArgs(null!).Exception != null) return 6;
                var interrupted = new[] { new ThreadInterruptedException(), new ThreadInterruptedException(null), new ThreadInterruptedException("interrupted", error) };
                foreach (var e in interrupted)
                {
                    if (e.HResult != unchecked((int)0x80131519) || string.IsNullOrEmpty(e.Message)) return 7;
                    try { throw e; } catch (SystemException caught) { if (!ReferenceEquals(caught, e)) return 8; }
                }
                if (interrupted[0].Message != interrupted[1].Message || interrupted[2].Message != "interrupted" || !ReferenceEquals(interrupted[2].InnerException, error)) return 9;
                var states = new[] { new ThreadStateException(), new ThreadStateException(null), new ThreadStateException("state", error) };
                foreach (var e in states) if (e.HResult != unchecked((int)0x80131520) || string.IsNullOrEmpty(e.Message)) return 10;
                if (states[0].Message != states[1].Message || states[2].Message != "state" || !ReferenceEquals(states[2].InnerException, error)) return 11;
                try { throw error; }
                catch (ThreadAbortException) { return 12; }
                catch (ThreadStartException) { return 13; }
                catch (Exception caught) { if (!ReferenceEquals(caught, error)) return 14; }
                return input + 1;
            }
        }
        public sealed class SerializedInterrupted : ThreadInterruptedException
        {
            public SerializedInterrupted() : base(null!, default(StreamingContext)) { }
        }
        public sealed class SerializedState : ThreadStateException
        {
            public SerializedState() : base(null!, default(StreamingContext)) { }
        }
        """;
}
