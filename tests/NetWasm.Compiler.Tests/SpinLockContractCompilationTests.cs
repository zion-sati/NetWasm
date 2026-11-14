using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SpinLockContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SpinLockOwnershipAndValidationMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("SpinLockFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "SpinLockFixture.EntryPoint",
            "Run", [new("run", "SpinLockFixture.EntryPoint", "Run")], Target: target,
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
        namespace SpinLockFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var spin = new SpinLock(false); var held = false; spin.Enter(ref held);
                var taken = false; var rejected = 0;
                try { spin.Enter(ref taken); } catch (PlatformNotSupportedException) { rejected++; }
                try { spin.TryEnter(1, ref taken); } catch (PlatformNotSupportedException) { rejected++; }
                try { spin.TryEnter(Timeout.InfiniteTimeSpan, ref taken); } catch (PlatformNotSupportedException) { rejected++; }
                if (taken || rejected != 3 || !spin.IsHeld) return 30;
                spin.Exit(); spin.TryEnter(ref taken); if (!taken) return 31; spin.Exit();
                try { new Serialized(); return 32; } catch (PlatformNotSupportedException) { }
                return input + 1;
            }
            public static int Portable(int input)
            {
                var tracked = default(SpinLock); var taken = false;
                if (!tracked.IsThreadOwnerTrackingEnabled || tracked.IsHeld || tracked.IsHeldByCurrentThread) return 1;
                try { tracked.Exit(); return 2; } catch (SynchronizationLockException) { }
                tracked.Enter(ref taken); if (!taken || !tracked.IsHeldByCurrentThread) return 3;
                taken = false;
                try { tracked.TryEnter(0, ref taken); return 4; } catch (LockRecursionException) { }
                if (taken || !tracked.IsHeld) return 5;
                tracked.Exit(false); if (tracked.IsHeld) return 6;
                tracked.TryEnter(TimeSpan.FromTicks(-19999), ref taken); if (!taken) return 7;
                tracked.Exit(true);
                taken = true;
                try { tracked.TryEnter(-2, ref taken); return 8; } catch (ArgumentException e) { if (e is ArgumentOutOfRangeException || taken) return 9; }
                taken = true;
                try { tracked.TryEnter(TimeSpan.FromMilliseconds(-2), ref taken); return 10; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "timeout" || (TimeSpan)e.ActualValue! != TimeSpan.FromMilliseconds(-2) || !taken) return 11; }
                taken = false;
                try { tracked.TryEnter(-2, ref taken); return 12; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "millisecondsTimeout" || (int)e.ActualValue! != -2) return 13; }
                var untracked = new SpinLock(false);
                if (untracked.IsThreadOwnerTrackingEnabled) return 14;
                try { _ = untracked.IsHeldByCurrentThread; return 15; } catch (InvalidOperationException) { }
                untracked.TryEnter(ref taken); if (!taken) return 16;
                taken = false; untracked.TryEnter(ref taken); if (taken) return 17;
                untracked.TryEnter(0, ref taken); if (taken) return 18;
                untracked.Exit(false); untracked.TryEnter(1, ref taken); if (!taken) return 19;
                untracked.Exit(true); taken = false; untracked.Enter(ref taken); if (!taken) return 20; untracked.Exit();
                var explicitTracked = new SpinLock(true); taken = false; explicitTracked.TryEnter(ref taken); if (!taken) return 21; explicitTracked.Exit();
                var inner = new Exception("inner"); var error = new LockRecursionException("recursion", inner);
                if (error.Message != "recursion" || !ReferenceEquals(error.InnerException, inner)) return 22;
                if (new LockRecursionException() is not Exception || new LockRecursionException("x").Message != "x") return 23;
                return input + 1;
            }
        }
        public sealed class Serialized : LockRecursionException
        {
            public Serialized() : base(null!, default(StreamingContext)) { }
        }
        """;
}
