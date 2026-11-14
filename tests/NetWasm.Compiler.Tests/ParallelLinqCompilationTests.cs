using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// PLINQ is dotnet/runtime deae2e32's System.Linq.Parallel whole. With one thread ParallelEnumerable.SinglePartitionMode
// (RuntimeFeature.IsMultithreadingSupported is false) runs each query as one partition without asynchronous channels,
// as on single-threaded browser and WASI builds. The fixture runs unchanged on desktop .NET 11.
public sealed class ParallelLinqCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void QueriesMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ParallelLinqFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ParallelLinqFixture.EntryPoint",
            "Run", [new("run", "ParallelLinqFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        namespace ParallelLinqFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var numbers = Enumerable.Range(0, 1000).ToArray();

                // Unordered aggregates.
                if (numbers.AsParallel().Where(x => x % 2 == 0).Select(x => (long)x * x).Sum() != 166167000) return 1;
                if (numbers.AsParallel().Min() != 0 || numbers.AsParallel().Max() != 999 || numbers.AsParallel().Average() != 499.5) return 2;
                if (numbers.AsParallel().Count(x => x > 500) != 499 || !numbers.AsParallel().Any(x => x == 7) || !numbers.AsParallel().All(x => x >= 0)) return 3;
                if (numbers.AsParallel().Aggregate(0L, (sum, x) => sum + x, (a, b) => a + b, total => total * 2) != 999000) return 4;
                if (numbers.AsParallel().Aggregate((a, b) => Math.Max(a, b)) != 999) return 5;

                // Ordered queries keep the source order.
                var squares = numbers.AsParallel().AsOrdered().Select(x => x * x).ToArray();
                if (!squares.SequenceEqual(numbers.Select(x => x * x))) return 6;
                if (numbers.AsParallel().AsOrdered().First(x => x > 10) != 11 || numbers.AsParallel().AsOrdered().Last(x => x < 10) != 9) return 7;
                if (numbers.AsParallel().AsOrdered().ElementAt(123) != 123 || numbers.AsParallel().AsOrdered().ElementAtOrDefault(5000) != 0) return 8;
                if (numbers.AsParallel().AsOrdered().FirstOrDefault(x => x > 5000) != 0 || numbers.AsParallel().AsOrdered().LastOrDefault() != 999) return 9;
                if (numbers.AsParallel().AsOrdered().Single(x => x == 500) != 500 || numbers.AsParallel().SingleOrDefault(x => x == 5000) != 0) return 10;
                if (!numbers.AsParallel().AsOrdered().Skip(990).Take(5).ToArray().SequenceEqual(new[] { 990, 991, 992, 993, 994 })) return 11;
                if (!numbers.AsParallel().AsOrdered().TakeWhile(x => x < 3).ToArray().SequenceEqual(new[] { 0, 1, 2 })) return 12;
                if (numbers.AsParallel().AsOrdered().SkipWhile(x => x < 995).Count() != 5) return 13;
                if (!numbers.AsParallel().OrderByDescending(x => x).ThenBy(x => x).Take(3).ToArray().SequenceEqual(new[] { 999, 998, 997 })) return 14;
                if (!numbers.AsParallel().AsOrdered().Reverse().Take(2).ToArray().SequenceEqual(new[] { 999, 998 })) return 15;

                // Set operations, grouping, joins and lookups.
                var groups = numbers.AsParallel().GroupBy(x => x % 3).OrderBy(g => g.Key).Select(g => g.Count()).ToArray();
                if (!groups.SequenceEqual(new[] { 334, 333, 333 })) return 16;
                if (numbers.AsParallel().Select(x => x % 10).Distinct().Count() != 10) return 17;
                if (numbers.AsParallel().Union(Enumerable.Range(990, 20).AsParallel()).Count() != 1010) return 18;
                if (numbers.AsParallel().Intersect(Enumerable.Range(990, 20).AsParallel()).Count() != 10) return 19;
                if (numbers.AsParallel().Except(Enumerable.Range(10, 1000).AsParallel()).Count() != 10) return 20;
                var joined = numbers.AsParallel().Join(Enumerable.Range(0, 10).AsParallel(), x => x, y => y * 100, (x, y) => x + y).Sum();
                if (joined != 4545) return 21;
                if (numbers.AsParallel().ToLookup(x => x % 2)[1].Count() != 500 || numbers.AsParallel().ToDictionary(x => x)[42] != 42) return 22;
                if (!numbers.AsParallel().AsOrdered().Zip(numbers.AsParallel().AsOrdered(), (x, y) => x + y).Take(3).ToArray().SequenceEqual(new[] { 0, 2, 4 })) return 23;
                if (!ParallelEnumerable.Range(0, 5).AsOrdered().Concat(ParallelEnumerable.Repeat(7, 2).AsOrdered()).ToArray().SequenceEqual(new[] { 0, 1, 2, 3, 4, 7, 7 })) return 24;
                if (!ParallelEnumerable.Empty<int>().DefaultIfEmpty(5).ToArray().SequenceEqual(new[] { 5 })) return 25;
                if (!numbers.AsParallel().AsOrdered().SequenceEqual(numbers.AsParallel().AsOrdered())) return 26;

                // Enumerating a query, ForAll and the merge options.
                long enumerated = 0;
                foreach (var x in numbers.AsParallel().Where(x => x < 100)) enumerated += x;
                if (enumerated != 4950) return 27;
                long all = 0;
                numbers.AsParallel().ForAll(x => Interlocked.Add(ref all, x));
                if (all != 499500) return 28;
                foreach (var options in new[] { ParallelMergeOptions.NotBuffered, ParallelMergeOptions.AutoBuffered, ParallelMergeOptions.FullyBuffered })
                {
                    var merged = numbers.AsParallel().AsOrdered().WithMergeOptions(options).Select(x => x + 1).ToList();
                    if (merged.Count != 1000 || merged[0] != 1 || merged[999] != 1000) return 29;
                    long sum = 0;
                    foreach (var x in numbers.AsParallel().WithMergeOptions(options).WithDegreeOfParallelism(2)) sum += x;
                    if (sum != 499500) return 30;
                }
                if (numbers.AsParallel().WithExecutionMode(ParallelExecutionMode.ForceParallelism).Where(x => x > 900).Count() != 99) return 31;

                // Cancellation and exceptions.
                try
                {
                    numbers.AsParallel().WithCancellation(new CancellationToken(true)).Select(x => x).ToArray();
                    return 32;
                }
                catch (OperationCanceledException) { }
                try
                {
                    numbers.AsParallel().Select(x => x == 500 ? throw new InvalidOperationException("five hundred") : x).ToArray();
                    return 33;
                }
                catch (AggregateException ex) when (ex.InnerExceptions.All(e => e.Message == "five hundred")) { }
                try { ParallelEnumerable.AsParallel((IEnumerable<int>)null!); return 34; } catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        """;
}
