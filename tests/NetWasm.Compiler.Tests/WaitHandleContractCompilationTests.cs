using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class WaitHandleContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WaitHandlesAndTheirProducers(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "WaitHandleFixture", Source, "WaitHandleFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    // The same program returns 42 on desktop .NET 11.
    private const string Source = """
        #pragma warning disable CS0618
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Win32.SafeHandles;
        namespace WaitHandleFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 3;
                using var handle = new ObservedWait();
                var disposed = 0;
                void Rejects(Func<object> wait) { try { wait(); } catch (ObjectDisposedException) { disposed++; } }
                Rejects(() => handle.WaitOne());
                Rejects(() => handle.WaitOne(0));
                Rejects(() => handle.WaitOne(0, false));
                Rejects(() => handle.WaitOne(TimeSpan.Zero));
                Rejects(() => handle.WaitOne(TimeSpan.Zero, false));
                Rejects(() => WaitHandle.SignalAndWait(handle, handle));
                Rejects(() => WaitHandle.SignalAndWait(handle, handle, 0, false));
                Rejects(() => WaitHandle.SignalAndWait(handle, handle, TimeSpan.Zero, false));
                Rejects(() => WaitHandle.WaitAll(new WaitHandle[] { handle }));
                Rejects(() => WaitHandle.WaitAll(new WaitHandle[] { handle }, 0));
                Rejects(() => WaitHandle.WaitAll(new WaitHandle[] { handle }, 0, false));
                Rejects(() => WaitHandle.WaitAll(new WaitHandle[] { handle }, TimeSpan.Zero));
                Rejects(() => WaitHandle.WaitAll(new WaitHandle[] { handle }, TimeSpan.Zero, false));
                Rejects(() => WaitHandle.WaitAny(new WaitHandle[] { handle }));
                Rejects(() => WaitHandle.WaitAny(new WaitHandle[] { handle }, 0));
                Rejects(() => WaitHandle.WaitAny(new WaitHandle[] { handle }, 0, false));
                Rejects(() => WaitHandle.WaitAny(new WaitHandle[] { handle }, TimeSpan.Zero));
                Rejects(() => WaitHandle.WaitAny(new WaitHandle[] { handle }, TimeSpan.Zero, false));
                if (disposed != 18) return 1;

                if (!((IAsyncResult)Task.CompletedTask).AsyncWaitHandle.WaitOne(0)) return 4;
                var pending = new TaskCompletionSource();
                var pendingHandle = ((IAsyncResult)pending.Task).AsyncWaitHandle;
                if (pendingHandle.WaitOne(0) || !ReferenceEquals(pendingHandle, ((IAsyncResult)pending.Task).AsyncWaitHandle)) return 5;
                pending.SetResult();
                if (!pendingHandle.WaitOne(0)) return 6;
                pending.Task.Dispose();
                try { _ = ((IAsyncResult)pending.Task).AsyncWaitHandle; return 7; } catch (ObjectDisposedException) { }

                if (CancellationToken.None.WaitHandle.WaitOne(0)) return 17;
                using (var cts = new CancellationTokenSource())
                {
                    var tokenHandle = cts.Token.WaitHandle;
                    if (tokenHandle.WaitOne(0) || !ReferenceEquals(tokenHandle, cts.Token.WaitHandle)) return 18;
                    cts.Cancel();
                    if (!tokenHandle.WaitOne(0)) return 19;
                }
                if (new CancellationToken(true).WaitHandle.WaitOne(0) == false) return 20;

                if (new HandleStream().CreateHandle().WaitOne(0)) return 21;
                if (new SafeWaitHandle(new IntPtr(42), true).IsInvalid) return 22;

                using (var slim = new ManualResetEventSlim(false))
                {
                    var slimHandle = slim.WaitHandle;
                    if (slimHandle.WaitOne(0)) return 23;
                    slim.Set();
                    if (!slimHandle.WaitOne(0) || !slim.Wait(0)) return 24;
                    slim.Reset();
                    if (slimHandle.WaitOne(0)) return 25;
                }
                using (var semaphore = new SemaphoreSlim(1, 2))
                {
                    var available = semaphore.AvailableWaitHandle;
                    if (!available.WaitOne(0)) return 26;
                    semaphore.Wait();
                    if (available.WaitOne(0)) return 27;
                    semaphore.Release();
                    if (!available.WaitOne(0) || semaphore.CurrentCount != 1) return 28;
                }
                using (var notify = new ManualResetEvent(false))
                {
                    var timer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
                    if (!timer.Dispose(notify) || !notify.WaitOne(0)) return 29;
                    if (timer.Dispose(new ManualResetEvent(false))) return 30;
                }
                return input + 1;
            }
            public static int Portable(int input)
            {
                var handle = new ObservedWait();
                if (handle.Handle != new IntPtr(-1) || !handle.SafeWaitHandle.IsInvalid) return 9;
                var borrowed = new SafeWaitHandle(new IntPtr(42), false);
                handle.SetSafeWaitHandle(borrowed);
                if (!ReferenceEquals(handle.GetSafeWaitHandle(), borrowed) || handle.Handle != new IntPtr(42)) return 10;
                handle.Handle = new IntPtr(-1);
                if (!borrowed.IsClosed || !handle.SafeWaitHandle.IsInvalid) return 11;
                var next = new SafeWaitHandle(new IntPtr(43), false); handle.SafeWaitHandle = next;
                handle.Dispose(); handle.Close();
                if (!next.IsClosed || handle.Disposals != 2) return 12;
                var custom = new CustomWait();
                if (!custom.WaitOne(0, false) || custom.WaitOne(1, false) || custom.Calls != 2) return 13;
                try { WaitHandleExtensions.GetSafeWaitHandle(null!); return 14; } catch (ArgumentNullException) { }
                try { WaitHandleExtensions.SetSafeWaitHandle(null!, null); return 15; } catch (ArgumentNullException) { }
                using var cts = new CancellationTokenSource();
                var called = 0;
                using var registration = cts.Token.Register(() => called++); cts.Cancel();
                if (!cts.Token.IsCancellationRequested || called != 1 || !((IAsyncResult)Task.CompletedTask).IsCompleted) return 16;
                return input + 1;
            }
        }
        public sealed class ObservedWait : WaitHandle
        {
            public int Disposals;
            protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
        }
        public sealed class CustomWait : WaitHandle
        {
            public int Calls;
            public override bool WaitOne(int millisecondsTimeout) { Calls++; return millisecondsTimeout == 0; }
        }
        public sealed class HandleStream : MemoryStream
        { public WaitHandle CreateHandle() => CreateWaitHandle(); }
        """;
}
