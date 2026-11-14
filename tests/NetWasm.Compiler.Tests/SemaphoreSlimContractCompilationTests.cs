using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SemaphoreSlimContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SemaphoreContractsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("SemaphoreFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "SemaphoreFixture.EntryPoint",
            "Run", [new("run", "SemaphoreFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        namespace SemaphoreFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var empty = new SemaphoreSlim(0, 1); var caught = 0;
                try { empty.Wait(); return 1; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Wait(CancellationToken.None); return 2; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Wait(1); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Wait(TimeSpan.FromMilliseconds(1)); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Wait(1, CancellationToken.None); return 5; } catch (PlatformNotSupportedException) { caught++; }
                try { empty.Wait(TimeSpan.FromMilliseconds(1), CancellationToken.None); return 6; } catch (PlatformNotSupportedException) { caught++; }
                if (empty.AvailableWaitHandle.WaitOne(0)) return 7; caught++;
                return caught == 7 && empty.CurrentCount == 0 ? input + 1 : 8;
            }
            public static int Portable(int input)
            {
                var semaphore = new SemaphoreSlim(1, 2);
                if (semaphore.CurrentCount != 1 || !semaphore.Wait(0) || semaphore.Wait(0) || semaphore.Wait(TimeSpan.Zero, CancellationToken.None)) return 9;
                if (semaphore.Release(2) != 0 || semaphore.CurrentCount != 2) return 10;
                semaphore.Wait(CancellationToken.None);
                if (!semaphore.Wait(TimeSpan.FromDays(30)) || semaphore.CurrentCount != 0) return 11;
                var first = semaphore.WaitAsync(); var second = semaphore.WaitAsync(CancellationToken.None);
                if (first.IsCompleted || second.IsCompleted) return 12;
                if (semaphore.Release(2) != 0 || !first.IsCompletedSuccessfully || !second.IsCompletedSuccessfully || semaphore.CurrentCount != 0) return 13;
                if (semaphore.Release() != 0 || !semaphore.WaitAsync().IsCompletedSuccessfully || semaphore.CurrentCount != 0) return 14;
                var canceled = new CancellationToken(true);
                if (!semaphore.WaitAsync(canceled).IsCanceled) return 15;
                try { semaphore.Wait(0, canceled); return 16; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 17; }
                semaphore.Release(2);
                try { semaphore.Release(); return 18; } catch (SemaphoreFullException) { }
                try { semaphore.Release(0); return 19; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "releaseCount") return 20; }
                try { new SemaphoreSlim(1, 0); return 21; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "initialCount") return 22; }
                try { new SemaphoreSlim(0, 0); return 23; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "maxCount") return 24; }
                var unbounded = new SemaphoreSlim(0); unbounded.Release(3);
                if (unbounded.CurrentCount != 3) return 25;
                var derived = new Derived(); derived.ReleaseResources(false);
                if (!derived.Wait(0)) return 26;
                derived.Release(); ((IDisposable)derived).Dispose(); derived.Dispose();
                if (derived.Disposals != 2 || derived.CurrentCount != 1) return 27;
                try { derived.Wait(0); return 28; } catch (ObjectDisposedException) { }
                try { derived.Wait(-2); return 29; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 30; }
                try { derived.Release(0); return 31; } catch (ObjectDisposedException) { }
                try { derived.WaitAsync(); return 32; } catch (ObjectDisposedException) { }
                try { _ = derived.AvailableWaitHandle; return 33; } catch (ObjectDisposedException) { }
                try { semaphore.Wait(TimeSpan.FromMilliseconds(-2)); return 34; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 35; }
                var cause = new Exception("cause"); var exception = new SemaphoreFullException("full", cause);
                if (exception.Message != "full" || !ReferenceEquals(exception.InnerException, cause)) return 36;
                return input + 1;
            }
        }
        public sealed class Derived : SemaphoreSlim
        {
            public int Disposals;
            public Derived() : base(1, 1) { }
            public void ReleaseResources(bool disposing) => base.Dispose(disposing);
            protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
        }
        """;
}
