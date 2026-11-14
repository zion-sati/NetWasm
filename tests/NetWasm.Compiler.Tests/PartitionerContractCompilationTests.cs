using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PartitionerContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PartitionerAdaptersMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "PartitionerFixture", Source, "PartitionerFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        namespace PartitionerFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Partitioner<int> plain = new Plain();
                if (plain.SupportsDynamicPartitions) return 1;
                try { plain.GetDynamicPartitions(); return 2; } catch (NotSupportedException) { }
                var plainParts = plain.GetPartitions(1);
                if (plainParts.Count != 1 || !plainParts[0].MoveNext() || plainParts[0].Current != 42) return 3;
                var absent = new NoDynamic();
                try { absent.GetOrderableDynamicPartitions(); return 4; } catch (NotSupportedException) { }
                try { absent.GetDynamicPartitions(); return 5; } catch (NotSupportedException) { }
                for (var flags = 0; flags != 8; flags++)
                {
                    var configured = new Ordered((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                    if (configured.KeysOrderedInEachPartition != ((flags & 1) != 0) || configured.KeysOrderedAcrossPartitions != ((flags & 2) != 0) || configured.KeysNormalized != ((flags & 4) != 0)) return 6;
                }
                var ordered = new Ordered(true, false, true);
                if (!ordered.SupportsDynamicPartitions) return 7;
                var partitions = ordered.GetPartitions(2);
                if (partitions.Count != 2) return 8;
                var iterator = partitions[0];
                if (!iterator.MoveNext() || iterator.Current != 42 || !Equals(((IEnumerator)iterator).Current, 42) || iterator.MoveNext()) return 9;
                iterator.Reset(); if (!iterator.MoveNext() || iterator.Current != 42) return 10;
                iterator.Dispose();
                if (ordered.First!.Resets != 1 || ordered.First.Disposals != 1) return 11;
                ordered.WrongCount = true;
                try { ordered.GetPartitions(2); return 12; } catch (InvalidOperationException) { }
                ordered.WrongCount = false;
                var dynamicValues = ordered.GetDynamicPartitions();
                using (var dynamicIterator = dynamicValues.GetEnumerator())
                    if (!dynamicIterator.MoveNext() || dynamicIterator.Current != 42 || dynamicIterator.MoveNext()) return 13;
                if (ordered.Dynamic.Last!.Disposals != 1) return 14;
                var nonGeneric = ((IEnumerable)dynamicValues).GetEnumerator();
                if (!nonGeneric.MoveNext() || !Equals(nonGeneric.Current, 42)) return 15;
                ((IDisposable)nonGeneric).Dispose();
                ((IDisposable)dynamicValues).Dispose();
                if (ordered.Dynamic.Disposals != 1) return 16;
                return input + 1;
            }
        }
        public sealed class Plain : Partitioner<int>
        {
            public override IList<IEnumerator<int>> GetPartitions(int count) => new[] { ((IEnumerable<int>)new[] { 42 }).GetEnumerator() };
        }
        public sealed class NoDynamic : OrderablePartitioner<int>
        {
            public NoDynamic() : base(false, false, false) { }
            public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int count) => Array.Empty<IEnumerator<KeyValuePair<long, int>>>();
        }
        public sealed class Ordered : OrderablePartitioner<int>
        {
            public bool WrongCount;
            public Tracker? First;
            public readonly DynamicSource Dynamic = new DynamicSource();
            public Ordered(bool each, bool across, bool normalized) : base(each, across, normalized) { }
            public override bool SupportsDynamicPartitions => true;
            public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int count)
            {
                if (WrongCount) return Array.Empty<IEnumerator<KeyValuePair<long, int>>>();
                var parts = new IEnumerator<KeyValuePair<long, int>>[count];
                for (var i = 0; i < count; i++) { var tracker = new Tracker(); if (i == 0) First = tracker; parts[i] = tracker; }
                return parts;
            }
            public override IEnumerable<KeyValuePair<long, int>> GetOrderableDynamicPartitions() => Dynamic;
        }
        public sealed class DynamicSource : IEnumerable<KeyValuePair<long, int>>, IDisposable
        {
            public Tracker? Last;
            public int Disposals;
            public IEnumerator<KeyValuePair<long, int>> GetEnumerator() => Last = new Tracker();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Dispose() => Disposals++;
        }
        public sealed class Tracker : IEnumerator<KeyValuePair<long, int>>
        {
            private int _index = -1;
            public int Resets;
            public int Disposals;
            public KeyValuePair<long, int> Current => new KeyValuePair<long, int>(123, 42);
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++_index == 0;
            public void Reset() { Resets++; _index = -1; }
            public void Dispose() => Disposals++;
        }
        """;
}
