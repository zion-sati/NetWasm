// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

// The trie holds what an array of every slot holds, through writes, meets and comparisons, for one leaf, one level of
// nodes and two; and a slot written and then forgotten is the same as one never written.
public sealed class LocalValueTrieTests
{
    private static readonly Func<int?, int?, int?> Meet = static (left, right) => left == right ? left : null;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(1_500)]
    [InlineData(40_000)]
    public void HoldsWhatAnArrayHolds(int slots)
    {
        var random = new Random(slots);
        var states = new List<(LocalValueTrie<int?> Trie, int?[] Array)> { (LocalValueTrie<int?>.Empty(slots), new int?[slots]) };
        for (var step = 0; step < 3_000; step++)
        {
            var (trie, array) = states[random.Next(states.Count)];
            if (random.Next(3) == 0)
            {
                var (otherTrie, otherArray) = states[random.Next(states.Count)];
                var met = trie.Meet(otherTrie, Meet);
                var metArray = array.Zip(otherArray, Meet).ToArray();
                Assert.Equal(array.SequenceEqual(metArray), ReferenceEquals(met, trie));
                states.Add((met, metArray));
            }
            else
            {
                // A few slots and a few values, so states meet on some slots and differ on others.
                var slot = random.Next(Math.Min(slots, 3)) * (slots / 3) + random.Next(Math.Min(slots, 2));
                int? value = random.Next(4) == 0 ? null : random.Next(3);
                var written = (int?[])array.Clone();
                written[slot] = value;
                states.Add((trie.Set(slot, value), written));
            }
        }
        for (var check = 0; check < 2_000; check++)
        {
            var (trie, array) = states[random.Next(states.Count)];
            var (otherTrie, otherArray) = states[random.Next(states.Count)];
            Assert.Equal(array.SequenceEqual(otherArray), trie.SameAs(otherTrie));
            var slot = random.Next(slots);
            Assert.Equal(array[slot], trie.Get(slot));
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(40_000)]
    public void ASlotWrittenAndForgottenIsOneNeverWritten(int slots)
    {
        var empty = LocalValueTrie<int?>.Empty(slots);
        var forgotten = empty.Set(slots - 1, 4).Set(slots - 1, null);
        var met = empty.Set(0, 4).Meet(empty.Set(0, 5), Meet);

        Assert.True(forgotten.SameAs(empty));
        Assert.True(met.SameAs(empty));
        Assert.Same(empty, empty.Set(3, null));
    }
}
