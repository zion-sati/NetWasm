using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ReaderWriterLockContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RecursiveOwnershipAndCookiesWork(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReaderWriterLockFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ReaderWriterLockFixture.EntryPoint",
            "Run", [new("run", "ReaderWriterLockFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        namespace ReaderWriterLockFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 30;
                var rw = new ReaderWriterLock();
                rw.AcquireReaderLock(0);
                var seq = rw.WriterSeqNum;
                var rejected = 0;
                try { rw.AcquireWriterLock(1); } catch (PlatformNotSupportedException) { rejected++; }
                try { rw.AcquireWriterLock(TimeSpan.FromMilliseconds(1)); } catch (PlatformNotSupportedException) { rejected++; }
                if (rejected != 2 || !rw.IsReaderLockHeld || rw.IsWriterLockHeld || rw.WriterSeqNum != seq) return 31;
                var cookie = rw.UpgradeToWriterLock(0);
                rw.DowngradeFromWriterLock(ref cookie);
                rw.ReleaseReaderLock();
                rw.AcquireWriterLock(0); rw.ReleaseWriterLock();
                return input + 1;
            }
            public static int Portable(int input)
            {
                var rw = new ReaderWriterLock();
                if (rw.IsReaderLockHeld || rw.IsWriterLockHeld) return 1;
                var seq = rw.WriterSeqNum;
                rw.AcquireReaderLock(0); rw.AcquireReaderLock(TimeSpan.Zero);
                if (!rw.IsReaderLockHeld || rw.AnyWritersSince(seq)) return 2;
                var upgraded = rw.UpgradeToWriterLock(TimeSpan.Zero);
                var copy = upgraded;
                if (!upgraded.Equals((object)copy) || upgraded != copy || upgraded.Equals(default(LockCookie))) return 3;
                if (!rw.IsWriterLockHeld || rw.IsReaderLockHeld) return 4;
                rw.DowngradeFromWriterLock(ref upgraded);
                if (upgraded == copy || !rw.IsReaderLockHeld || rw.IsWriterLockHeld) return 5;
                rw.ReleaseReaderLock(); if (!rw.IsReaderLockHeld) return 6;
                var released = rw.ReleaseLock();
                if (rw.IsReaderLockHeld) return 7;
                rw.RestoreLock(ref released); rw.ReleaseReaderLock();
                if (rw.IsReaderLockHeld || !rw.AnyWritersSince(seq)) return 8;
                rw.AcquireWriterLock(TimeSpan.Zero); rw.AcquireWriterLock(0);
                rw.AcquireReaderLock(0); rw.ReleaseReaderLock();
                var writer = rw.ReleaseLock();
                if (rw.IsWriterLockHeld) return 9;
                rw.RestoreLock(ref writer); rw.ReleaseWriterLock();
                if (!rw.IsWriterLockHeld) return 10;
                rw.ReleaseWriterLock();
                if (rw.IsWriterLockHeld) return 11;
                var none = rw.ReleaseLock(); rw.RestoreLock(ref none);
                var emptyUpgrade = rw.UpgradeToWriterLock(0); rw.DowngradeFromWriterLock(ref emptyUpgrade);
                if (rw.IsWriterLockHeld || rw.IsReaderLockHeld) return 12;
                try { rw.AcquireReaderLock(-2); return 13; } catch (ArgumentOutOfRangeException) { }
                try { rw.AcquireWriterLock(TimeSpan.FromMilliseconds(-2)); return 14; } catch (ArgumentOutOfRangeException) { }
                try { rw.UpgradeToWriterLock(-2); return 15; } catch (ArgumentOutOfRangeException) { }
                try { rw.ReleaseReaderLock(); return 16; } catch (ApplicationException) { }
                try { rw.ReleaseWriterLock(); return 17; } catch (ApplicationException) { }
                var invalid = default(LockCookie);
                try { rw.RestoreLock(ref invalid); return 18; } catch (ApplicationException) { }
                rw.AcquireReaderLock(0);
                try { rw.RestoreLock(ref none); return 19; } catch (SynchronizationLockException) { }
                try { rw.AcquireWriterLock(0); return 20; } catch (ApplicationException) { }
                if (!rw.IsReaderLockHeld || rw.IsWriterLockHeld) return 21;
                rw.ReleaseReaderLock();
                return input + 1;
            }
        }
        """;
}
