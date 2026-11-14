using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SpinWaitContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SpinWaitImmediateChecksAndRejections(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("SpinWaitFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "SpinWaitFixture.EntryPoint",
            "Run", [new("run", "SpinWaitFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Threading;
        namespace SpinWaitFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var spinner = new SpinWait(); var rejected = 0;
                try { spinner.SpinOnce(); } catch (PlatformNotSupportedException e) { if (e.Message.Contains("yielding")) rejected++; }
                try { spinner.SpinOnce(-1); } catch (PlatformNotSupportedException) { rejected++; }
                try { spinner.SpinOnce(0); } catch (PlatformNotSupportedException) { rejected++; }
                try { spinner.SpinOnce(int.MaxValue); } catch (PlatformNotSupportedException) { rejected++; }
                var calls = 0;
                try { SpinWait.SpinUntil(() => { calls++; return false; }); } catch (PlatformNotSupportedException) { rejected++; }
                try { SpinWait.SpinUntil(() => { calls++; return false; }, 1); } catch (PlatformNotSupportedException) { rejected++; }
                try { SpinWait.SpinUntil(() => { calls++; return false; }, Timeout.InfiniteTimeSpan); } catch (PlatformNotSupportedException) { rejected++; }
                if (rejected != 7 || calls != 3 || spinner.Count != 0 || !spinner.NextSpinWillYield) return 30;
                spinner.Reset(); if (spinner.Count != 0) return 31;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var spinner = default(SpinWait); if (spinner.Count != 0) return 1;
                spinner.Reset(); if (spinner.Count != 0) return 2;
                var calls = 0;
                SpinWait.SpinUntil(() => { calls++; return true; });
                if (!SpinWait.SpinUntil(() => { calls++; return true; }, -1)) return 3;
                if (!SpinWait.SpinUntil(() => { calls++; return true; }, TimeSpan.FromTicks(-19999))) return 4;
                if (calls != 3) return 5;
                if (SpinWait.SpinUntil(() => { calls++; return false; }, 0)) return 6;
                if (SpinWait.SpinUntil(() => { calls++; return false; }, TimeSpan.FromTicks(9999))) return 7;
                if (calls != 5) return 8;
                try { SpinWait.SpinUntil(null!); return 9; } catch (ArgumentNullException e) { if (e.ParamName != "condition") return 10; }
                try { SpinWait.SpinUntil(null!, -2); return 11; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "millisecondsTimeout" || (int)e.ActualValue! != -2) return 12; }
                try { SpinWait.SpinUntil(null!, TimeSpan.FromMilliseconds(-2)); return 13; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "timeout" || (TimeSpan)e.ActualValue! != TimeSpan.FromMilliseconds(-2)) return 14; }
                try { SpinWait.SpinUntil(null!, TimeSpan.FromDays(30)); return 15; } catch (ArgumentOutOfRangeException) { }
                try { spinner.SpinOnce(-2); return 16; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "sleep1Threshold" || (int)e.ActualValue! != -2) return 17; }
                var expected = new InvalidOperationException("predicate");
                try { SpinWait.SpinUntil(() => throw expected, 0); return 18; } catch (InvalidOperationException e) { if (!ReferenceEquals(e, expected)) return 19; }
                if (spinner.Count != 0) return 20;
                return input + 1;
            }
        }
        """;
}
