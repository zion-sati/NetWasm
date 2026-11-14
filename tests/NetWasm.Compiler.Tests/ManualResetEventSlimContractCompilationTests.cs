using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManualResetEventSlimContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EventStateAndImmediateWaitsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ManualEventFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ManualEventFixture.EntryPoint",
            "Run", [new("run", "ManualEventFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        namespace ManualEventFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                using var pending = new ManualResetEventSlim();
                var rejected = 0;
                try { pending.Wait(); } catch (PlatformNotSupportedException) { rejected++; }
                try { pending.Wait(CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                try { pending.Wait(1); } catch (PlatformNotSupportedException) { rejected++; }
                try { pending.Wait(TimeSpan.FromMilliseconds(1)); } catch (PlatformNotSupportedException) { rejected++; }
                try { pending.Wait(-1, CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                try { pending.Wait(Timeout.InfiniteTimeSpan, CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                if (!pending.WaitHandle.WaitOne(0)) rejected++;
                if (rejected != 7 || pending.IsSet || pending.SpinCount != 1) return 30;
                pending.Set(); if (!pending.Wait(0) || !pending.WaitHandle.WaitOne(0)) return 31;
                return input + 1;
            }
            public static int Portable(int input)
            {
                using var e = new ManualResetEventSlim(false, 31);
                if (e.IsSet || e.SpinCount != (Environment.ProcessorCount == 1 ? 1 : 31)) return 1;
                if (e.Wait(0) || e.Wait(TimeSpan.Zero) || e.Wait(0, CancellationToken.None) || e.Wait(TimeSpan.Zero, CancellationToken.None)) return 2;
                e.Set(); e.Set();
                e.Wait(); e.Wait(CancellationToken.None);
                if (!e.Wait(-1) || !e.Wait(Timeout.InfiniteTimeSpan) || !e.Wait(1, CancellationToken.None) || !e.Wait(TimeSpan.FromTicks(-19999), CancellationToken.None)) return 3;
                e.Reset(); if (e.IsSet) return 4;
                using var initiallySet = new ManualResetEventSlim(true); initiallySet.Wait();
                var canceled = new CancellationToken(true);
                try { initiallySet.Wait(-2, canceled); return 5; } catch (OperationCanceledException x) { if (x.CancellationToken != canceled) return 6; }
                try { e.Wait(-2); return 7; } catch (ArgumentOutOfRangeException x) { if (x.ParamName != "millisecondsTimeout") return 8; }
                try { e.Wait(TimeSpan.FromMilliseconds(-2), canceled); return 9; } catch (ArgumentOutOfRangeException x) { if (x.ParamName != "timeout") return 10; }
                try { e.Wait(TimeSpan.FromDays(30)); return 11; } catch (ArgumentOutOfRangeException) { }
                try { new ManualResetEventSlim(false, -1); return 12; } catch (ArgumentOutOfRangeException x) { if (x.ParamName != "spinCount") return 13; }
                try { new ManualResetEventSlim(false, 2048); return 14; } catch (ArgumentOutOfRangeException) { }
                using var max = new ManualResetEventSlim(false, 2047);
                e.Dispose(); e.Set();
                if (!e.IsSet) return 15;
                try { e.Reset(); return 16; } catch (ObjectDisposedException) { }
                try { e.Wait(-2, canceled); return 17; } catch (ObjectDisposedException) { }
                try { e.Wait(TimeSpan.FromMilliseconds(-2), canceled); return 18; } catch (ArgumentOutOfRangeException) { }
                try { _ = e.WaitHandle; return 19; } catch (ObjectDisposedException) { }
                var derived = new Derived(); derived.Release(false);
                try { derived.Wait(0); return 20; } catch (ObjectDisposedException) { }
                ((IDisposable)derived).Dispose(); derived.Dispose();
                if (derived.Calls != 3) return 21;
                return input + 1;
            }
        }
        public sealed class Derived : ManualResetEventSlim
        {
            public int Calls;
            public void Release(bool disposing) => Dispose(disposing);
            protected override void Dispose(bool disposing) { Calls++; base.Dispose(disposing); }
        }
        """;
}
