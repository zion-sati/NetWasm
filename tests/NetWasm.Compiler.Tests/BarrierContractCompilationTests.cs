using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BarrierContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BarrierPhaseStateAndRollbackMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("BarrierFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "BarrierFixture.EntryPoint",
            "Run", [new("run", "BarrierFixture.EntryPoint", "Run")], Target: target,
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
        namespace BarrierFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                using var waiting = new Barrier(2);
                var rejected = 0;
                try { waiting.SignalAndWait(); } catch (PlatformNotSupportedException) { rejected++; }
                if (waiting.ParticipantsRemaining != 2 || waiting.CurrentPhaseNumber != 0) return 30;
                try { waiting.SignalAndWait(CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                try { waiting.SignalAndWait(1); } catch (PlatformNotSupportedException) { rejected++; }
                try { waiting.SignalAndWait(TimeSpan.FromMilliseconds(1)); } catch (PlatformNotSupportedException) { rejected++; }
                try { waiting.SignalAndWait(-1, CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                try { waiting.SignalAndWait(Timeout.InfiniteTimeSpan, CancellationToken.None); } catch (PlatformNotSupportedException) { rejected++; }
                if (rejected != 6 || waiting.ParticipantsRemaining != 2 || waiting.CurrentPhaseNumber != 0) return 31;
                waiting.RemoveParticipant(); if (!waiting.SignalAndWait(0) || waiting.CurrentPhaseNumber != 1) return 32;
                try { new Serialized(); return 33; } catch (PlatformNotSupportedException) { }
                return input + 1;
            }
            public static int Portable(int input)
            {
                using var one = new Barrier(1);
                one.SignalAndWait(); one.SignalAndWait(CancellationToken.None);
                if (!one.SignalAndWait(0) || !one.SignalAndWait(TimeSpan.Zero) || !one.SignalAndWait(-1, CancellationToken.None) || !one.SignalAndWait(Timeout.InfiniteTimeSpan, CancellationToken.None)) return 1;
                if (one.CurrentPhaseNumber != 6 || one.ParticipantCount != 1 || one.ParticipantsRemaining != 1) return 2;
                if (one.AddParticipant() != 6 || one.AddParticipants(2) != 6 || one.ParticipantCount != 4) return 3;
                if (one.SignalAndWait(0) || one.ParticipantsRemaining != 4 || one.CurrentPhaseNumber != 6) return 4;
                one.RemoveParticipants(2); one.RemoveParticipant(); if (!one.SignalAndWait(0)) return 5;
                using var zero = new Barrier(0);
                try { zero.SignalAndWait(0); return 6; } catch (InvalidOperationException) { }
                if (zero.AddParticipant() != 0 || !zero.SignalAndWait(0)) return 7;
                var local = new AsyncLocal<int>(); local.Value = 7;
                var actions = 0; var prohibited = 0; var expected = new ArgumentException("phase");
                using var action = new Barrier(1, b =>
                {
                    actions++;
                    if (local.Value != 7) throw new Exception("context");
                    local.Value = 11;
                    try { b.AddParticipant(); } catch (InvalidOperationException) { prohibited++; }
                    try { b.RemoveParticipant(); } catch (InvalidOperationException) { prohibited++; }
                    try { b.SignalAndWait(0); } catch (InvalidOperationException) { prohibited++; }
                    try { b.Dispose(); } catch (InvalidOperationException) { prohibited++; }
                    if (actions == 1) throw expected;
                });
                local.Value = 9;
                try { action.SignalAndWait(0); return 8; } catch (BarrierPostPhaseException e) { if (!ReferenceEquals(e.InnerException, expected)) return 9; }
                if (action.CurrentPhaseNumber != 1 || action.ParticipantsRemaining != 1 || local.Value != 9) return 10;
                if (!action.SignalAndWait(0) || action.CurrentPhaseNumber != 2 || actions != 2 || prohibited != 8) return 11;
                var canceled = new CancellationToken(true);
                try { one.SignalAndWait(-2, canceled); return 12; } catch (OperationCanceledException e) { if (e.CancellationToken != canceled) return 13; }
                try { one.SignalAndWait(TimeSpan.FromMilliseconds(-2), canceled); return 14; } catch (ArgumentOutOfRangeException) { }
                try { new Barrier(-1); return 15; } catch (ArgumentOutOfRangeException) { }
                try { new Barrier(32768); return 16; } catch (ArgumentOutOfRangeException) { }
                using var max = new Barrier(32767);
                try { max.AddParticipant(); return 17; } catch (InvalidOperationException) { }
                try { one.AddParticipants(0); return 18; } catch (ArgumentOutOfRangeException) { }
                try { one.RemoveParticipants(2); return 19; } catch (ArgumentOutOfRangeException) { }
                var failure = new BarrierPostPhaseException(expected);
                if (!ReferenceEquals(failure.InnerException, expected) || new BarrierPostPhaseException("x").Message != "x" || new BarrierPostPhaseException("y", expected).Message != "y" || string.IsNullOrEmpty(new BarrierPostPhaseException().Message)) return 20;
                one.Dispose(); one.Dispose();
                try { one.SignalAndWait(-2, canceled); return 21; } catch (ObjectDisposedException) { }
                try { one.AddParticipants(0); return 22; } catch (ObjectDisposedException) { }
                var derived = new Derived(); derived.Release(false);
                try { derived.AddParticipant(); return 23; } catch (ObjectDisposedException) { }
                derived.Dispose(); if (derived.Calls != 2) return 24;
                return input + 1;
            }
        }
        public sealed class Derived : Barrier
        {
            public Derived() : base(1) { }
            public int Calls;
            public void Release(bool disposing) => Dispose(disposing);
            protected override void Dispose(bool disposing) { Calls++; base.Dispose(disposing); }
        }
        public sealed class Serialized : BarrierPostPhaseException
        {
            public Serialized() : base(null!, default(StreamingContext)) { }
        }
        """;
}
