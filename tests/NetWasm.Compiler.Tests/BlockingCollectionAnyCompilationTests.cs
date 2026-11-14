using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BlockingCollectionAnyCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AnyCollectionOperationsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "BlockingAnyFixture", Source, "BlockingAnyFixture.EntryPoint", false, target, 41, [])
        {
            Optimization = FinalWasmOptimization.None,
            // Upstream's BlockingCollection reads the monotonic clock for its timeouts.
            Exports = [new RequestedExport("run", "BlockingAnyFixture.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
        }));
    }

    private const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Threading;
        namespace BlockingAnyFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var full = new[] { new BlockingCollection<int>(1) }; full[0].Add(1);
                var empty = new[] { new BlockingCollection<int>() }; var caught = 0;
                try { BlockingCollection<int>.AddToAny(full, 2); return 1; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.AddToAny(full, 2, CancellationToken.None); return 2; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.TryAddToAny(full, 2, 1); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.TryAddToAny(full, 2, TimeSpan.FromMilliseconds(1)); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.TakeFromAny(empty, out _); return 5; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.TakeFromAny(empty, out _, CancellationToken.None); return 6; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.TryTakeFromAny(empty, out _, 1); return 7; } catch (PlatformNotSupportedException) { caught++; }
                try { BlockingCollection<int>.TryTakeFromAny(empty, out _, TimeSpan.FromMilliseconds(1)); return 8; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 8 && full[0].Count == 1 && empty[0].Count == 0 ? input + 1 : 9;
            }
            public static int Portable(int input)
            {
                var single = new[] { new BlockingCollection<int>() };
                for (var variant = 0; variant < 6; variant++)
                {
                    var added = variant switch
                    {
                        0 => BlockingCollection<int>.AddToAny(single, 42),
                        1 => BlockingCollection<int>.AddToAny(single, 42, CancellationToken.None),
                        2 => BlockingCollection<int>.TryAddToAny(single, 42),
                        3 => BlockingCollection<int>.TryAddToAny(single, 42, TimeSpan.Zero),
                        4 => BlockingCollection<int>.TryAddToAny(single, 42, 0),
                        _ => BlockingCollection<int>.TryAddToAny(single, 42, 0, CancellationToken.None)
                    };
                    var value = 0;
                    var taken = variant switch
                    {
                        0 => BlockingCollection<int>.TakeFromAny(single, out value),
                        1 => BlockingCollection<int>.TakeFromAny(single, out value, CancellationToken.None),
                        2 => BlockingCollection<int>.TryTakeFromAny(single, out value),
                        3 => BlockingCollection<int>.TryTakeFromAny(single, out value, TimeSpan.Zero),
                        4 => BlockingCollection<int>.TryTakeFromAny(single, out value, 0),
                        _ => BlockingCollection<int>.TryTakeFromAny(single, out value, 0, CancellationToken.None)
                    };
                    if (added != 0 || taken != 0 || value != 42) return 10;
                }
                var canceled = new CancellationToken(true);
                var bounded = new BlockingCollection<int>(1); var unbounded = new BlockingCollection<int>();
                var mixed = new[] { bounded, unbounded };
                if (BlockingCollection<int>.TryAddToAny(mixed, 42, 0, canceled) != 1 || bounded.Count != 0) return 11;
                try { BlockingCollection<int>.TryTakeFromAny(mixed, out _, 0, canceled); return 12; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 13; }
                if (BlockingCollection<int>.TakeFromAny(mixed, out var item) != 1 || item != 42) return 14;
                var allBounded = new[] { bounded, new BlockingCollection<int>(1) };
                try { BlockingCollection<int>.TryAddToAny(allBounded, 1, 0, canceled); return 15; } catch (OperationCanceledException) { }
                if (BlockingCollection<int>.TryAddToAny(allBounded, 20) != 0 || BlockingCollection<int>.TryAddToAny(allBounded, 22) != 1 || BlockingCollection<int>.TryAddToAny(allBounded, 0) != -1) return 16;
                if (BlockingCollection<int>.TryTakeFromAny(allBounded, out item) != 0 || item != 20 || BlockingCollection<int>.TryTakeFromAny(allBounded, out item) != 1 || item != 22 || BlockingCollection<int>.TryTakeFromAny(allBounded, out item) != -1 || item != 0) return 17;
                foreach (var collection in allBounded) collection.CompleteAdding();
                if (BlockingCollection<int>.TryTakeFromAny(allBounded, out item, -1) != -1) return 18;
                try { BlockingCollection<int>.TakeFromAny(allBounded, out item); return 19; } catch (ArgumentException ex) { if (ex.ParamName != "collections") return 20; }
                try { BlockingCollection<int>.TryAddToAny(allBounded, 1, 0, canceled); return 21; } catch (ArgumentException ex) { if (ex.ParamName != "collections") return 22; }
                var disposed = new BlockingCollection<int>(); disposed.Dispose();
                try { BlockingCollection<int>.AddToAny(new[] { unbounded, disposed }, 1); return 23; } catch (ObjectDisposedException ex) { if (ex.ObjectName != "collections") return 24; }
                try { BlockingCollection<int>.TakeFromAny(new BlockingCollection<int>[] { null! }, out item); return 25; } catch (ArgumentException ex) { if (ex.ParamName != "collections") return 26; }
                try { BlockingCollection<int>.TryAddToAny(null!, 1, -2); return 27; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 28; }
                try { BlockingCollection<int>.TryTakeFromAny(single, out item, TimeSpan.FromMilliseconds(-2)); return 29; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 30; }
                try { BlockingCollection<int>.TryAddToAny(Array.Empty<BlockingCollection<int>>(), 1); return 31; } catch (ArgumentException) { }
                try { BlockingCollection<int>.TryTakeFromAny(new BlockingCollection<int>[64], out item); return 32; } catch (ArgumentOutOfRangeException) { }
                var sixtyThree = new BlockingCollection<int>[63]; for (var i = 0; i < 63; i++) sixtyThree[i] = unbounded;
                if (BlockingCollection<int>.TryTakeFromAny(sixtyThree, out item) != -1) return 33;
                return input + 1;
            }
        }
        """;
}
