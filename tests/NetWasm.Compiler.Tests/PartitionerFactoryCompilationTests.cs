using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PartitionerFactoryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FactoriesMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "PartitionerFactoryFixture", Source, "PartitionerFactoryFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Concurrent;
        namespace PartitionerFactoryFixture;
        public static class EntryPoint
        {
            private static int Reads;
            private static int Disposals;
            private static IEnumerable<int> Source()
            {
                try { for (var i = 0; i < 7; i++) { Reads++; yield return i; } }
                finally { Disposals++; }
            }
            public static int Run(int input)
            {
                var array = new[] { 0, 1, 2, 3, 4, 5, 6 };
                foreach (var balance in new[] { false, true })
                {
                    if (!Check(Partitioner.Create(array, balance)) || !Check(Partitioner.Create((IList<int>)new List<int>(array), balance))) return 1;
                }
                if (!Check(Partitioner.Create((IEnumerable<int>)array)) || !Check(Partitioner.Create((IEnumerable<int>)array, EnumerablePartitionerOptions.NoBuffering))) return 2;
                var dynamicSource = Partitioner.Create(Source(), EnumerablePartitionerOptions.NoBuffering).GetOrderableDynamicPartitions();
                var first = dynamicSource.GetEnumerator(); var second = dynamicSource.GetEnumerator();
                if (!first.MoveNext() || first.Current.Value != 0 || Reads != 1) return 3;
                if (!second.MoveNext() || second.Current.Value != 1 || Reads != 2) return 4;
                first.Dispose(); second.Dispose(); ((IDisposable)dynamicSource).Dispose();
                if (Disposals != 1) return 5;
                long width = 0; var count = 0;
                var ranges = Partitioner.Create(int.MinValue, int.MaxValue, int.MaxValue).GetDynamicPartitions();
                foreach (var range in ranges) { width += (long)range.Item2 - range.Item1; count++; }
                ((IDisposable)ranges).Dispose();
                if (width != 4294967295L || count != 3) return 6;
                var longRanges = Partitioner.Create(long.MaxValue - 5, long.MaxValue, 2L).GetDynamicPartitions();
                width = 0; count = 0;
                foreach (var range in longRanges) { width += range.Item2 - range.Item1; count++; }
                ((IDisposable)longRanges).Dispose(); if (width != 5 || count != 3) return 7;
                var defaults = Partitioner.Create(0, 7).GetDynamicPartitions(); var sum = 0;
                foreach (var range in defaults) sum += range.Item2 - range.Item1;
                ((IDisposable)defaults).Dispose(); if (sum != 7) return 8;
                var longDefaults = Partitioner.Create(0L, 7L).GetDynamicPartitions(); width = 0;
                foreach (var range in longDefaults) width += range.Item2 - range.Item1;
                ((IDisposable)longDefaults).Dispose(); if (width != 7) return 9;
                try { Partitioner.Create((int[])null!, false); return 10; } catch (ArgumentNullException ex) { if (ex.ParamName != "array") return 11; }
                try { Partitioner.Create((IList<int>)null!, false); return 12; } catch (ArgumentNullException ex) { if (ex.ParamName != "list") return 13; }
                try { Partitioner.Create((IEnumerable<int>)null!); return 14; } catch (ArgumentNullException ex) { if (ex.ParamName != "source") return 15; }
                try { Partitioner.Create((IEnumerable<int>)array, (EnumerablePartitionerOptions)2); return 16; } catch (ArgumentOutOfRangeException) { }
                try { Partitioner.Create(1, 1); return 17; } catch (ArgumentOutOfRangeException) { }
                try { Partitioner.Create(0L, 1L, 0L); return 18; } catch (ArgumentOutOfRangeException) { }
                try { Partitioner.Create(array, false).GetPartitions(0); return 19; } catch (ArgumentOutOfRangeException) { }
                try { Partitioner.Create(array, false).GetDynamicPartitions(); return 20; } catch (NotSupportedException) { }
                return input + 1;
            }
            private static bool Check(OrderablePartitioner<int> partitioner)
            {
                var partitions = partitioner.GetOrderablePartitions(3);
                var mask = 0; var count = 0; var progress = true;
                while (progress)
                {
                    progress = false;
                    foreach (var iterator in partitions)
                    {
                        if (!iterator.MoveNext()) continue;
                        progress = true; var pair = iterator.Current;
                        if (pair.Key != pair.Value || (mask & (1 << pair.Value)) != 0) return false;
                        mask |= 1 << pair.Value; count++;
                    }
                }
                foreach (var iterator in partitions) iterator.Dispose();
                return mask == 127 && count == 7 && partitioner.KeysNormalized && partitioner.KeysOrderedInEachPartition;
            }
        }
        """;
}
