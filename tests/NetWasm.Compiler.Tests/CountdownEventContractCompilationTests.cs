using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CountdownEventContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CountdownStateAndValidationMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CountdownFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CountdownFixture.EntryPoint",
            "Run", [new("run", "CountdownFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        namespace CountdownFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                using var e = new CountdownEvent(1);
                var rejected = 0;
                try { e.Wait(); } catch (PlatformNotSupportedException) { rejected++; }
                try { e.Wait(CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                try { e.Wait(1); } catch (PlatformNotSupportedException) { rejected++; }
                try { e.Wait(TimeSpan.FromMilliseconds(1)); } catch (PlatformNotSupportedException) { rejected++; }
                try { e.Wait(-1, CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                try { e.Wait(Timeout.InfiniteTimeSpan, CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                if (!e.WaitHandle.WaitOne(0)) rejected++;
                if (rejected != 7 || !e.Signal() || !e.Wait(0) || !e.WaitHandle.WaitOne(0)) return 30;
                return input + 1;
            }
            public static int Portable(int input)
            {
                using var e = new CountdownEvent(2);
                if (e.InitialCount != 2 || e.CurrentCount != 2 || e.IsSet) return 1;
                if (e.Wait(0) || e.Wait(TimeSpan.Zero) || e.Wait(0, CancellationToken.None) || e.Wait(TimeSpan.Zero, CancellationToken.None)) return 2;
                e.AddCount(); e.AddCount(2);
                if (!e.TryAddCount() || !e.TryAddCount(2) || e.CurrentCount != 8 || e.InitialCount != 2) return 3;
                if (e.Signal() || e.Signal(6) || e.CurrentCount != 1 || !e.Signal() || !e.IsSet) return 4;
                e.Wait(); e.Wait(CancellationToken.None);
                if (!e.Wait(-1) || !e.Wait(Timeout.InfiniteTimeSpan) || !e.Wait(-1, CancellationToken.None) || !e.Wait(Timeout.InfiniteTimeSpan, CancellationToken.None)) return 5;
                if (e.TryAddCount() || e.TryAddCount(1)) return 6;
                try { e.AddCount(); return 7; } catch (InvalidOperationException) { }
                try { e.Signal(); return 8; } catch (InvalidOperationException) { }
                e.Reset(); if (e.CurrentCount != 2 || e.IsSet) return 9;
                try { e.Signal(3); return 10; } catch (InvalidOperationException) { }
                if (e.CurrentCount != 2) return 11;
                e.Reset(0); if (!e.IsSet || e.InitialCount != 0) return 12;
                e.Reset(3); e.Signal(); e.Reset(); if (e.CurrentCount != 3 || e.InitialCount != 3) return 13;
                using var max = new CountdownEvent(int.MaxValue);
                try { max.AddCount(); return 14; } catch (InvalidOperationException) { }
                if (max.CurrentCount != int.MaxValue) return 15;
                var canceled = new CancellationToken(true);
                try { e.Wait(-2, canceled); return 16; } catch (ArgumentOutOfRangeException x) { if (x.ParamName != "millisecondsTimeout") return 17; }
                try { e.Wait(0, canceled); return 18; } catch (OperationCanceledException x) { if (x.CancellationToken != canceled) return 19; }
                try { new CountdownEvent(-1); return 20; } catch (ArgumentOutOfRangeException x) { if (x.ParamName != "initialCount") return 21; }
                e.Dispose();
                try { e.Signal(0); return 22; } catch (ArgumentOutOfRangeException) { }
                try { e.TryAddCount(0); return 23; } catch (ArgumentOutOfRangeException) { }
                try { e.Reset(-1); return 24; } catch (ObjectDisposedException) { }
                try { e.Wait(-2, canceled); return 25; } catch (ArgumentOutOfRangeException) { }
                try { e.Wait(0, canceled); return 26; } catch (ObjectDisposedException) { }
                try { _ = e.WaitHandle; return 27; } catch (ObjectDisposedException) { }
                if (e.InitialCount != 3 || e.CurrentCount != 3 || e.IsSet) return 28;
                var derived = new Derived(); derived.Release(false);
                if (!derived.Signal()) return 29;
                ((IDisposable)derived).Dispose(); derived.Dispose();
                if (derived.Calls != 3) return 32;
                return input + 1;
            }
        }
        public sealed class Derived : CountdownEvent
        {
            public Derived() : base(1) { }
            public int Calls;
            public void Release(bool disposing) => Dispose(disposing);
            protected override void Dispose(bool disposing) { Calls++; base.Dispose(disposing); }
        }
        """;
}
