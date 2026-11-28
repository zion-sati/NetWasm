// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Threading;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// A concurrent memo of one byte per ordered pair of types, compact enough for the hundred million pairs a large
/// program's classification asks: as a ConcurrentDictionary keyed by the two type identities it took 7.4 GB of an
/// 11.6 GB heap on Devolutions' SampleApp (about 70 bytes per pair for a three-bit answer). Each type gets a dense
/// number once, a pair is a 64-bit key, and the keys live in open-addressed tables split into stripes, about nine bytes
/// per pair. Reads take no lock: a stripe publishes a table only once it is filled, and a slot's value is written before
/// its key, so a reader that finds a key finds its value, and one holding an older table at worst misses and computes
/// the answer again. Writes lock their stripe.
/// </summary>
internal sealed class TypePairMemo
{
    private const int StripeBits = 8;
    private const int InitialCapacity = 64;

    private readonly ConcurrentDictionary<CliTypeIdentity, int> _numbers = new();
    private readonly Stripe[] _stripes = new Stripe[1 << StripeBits];
    private int _lastNumber;

    internal TypePairMemo()
    {
        for (var stripe = 0; stripe < _stripes.Length; stripe++) _stripes[stripe] = new();
    }

    // The pairs remembered.
    internal long Count
    {
        get
        {
            long count = 0;
            foreach (var stripe in _stripes) count += Volatile.Read(ref stripe.Count);
            return count;
        }
    }

    internal bool TryGet(CliTypeIdentity first, CliTypeIdentity second, out byte value)
    {
        if (_numbers.TryGetValue(first, out var a) && _numbers.TryGetValue(second, out var b))
        {
            var key = Key(a, b);
            var hash = Hash(key);
            var table = Volatile.Read(ref _stripes[StripeOf(hash)].Table);
            for (var slot = SlotOf(hash, table); ; slot = (slot + 1) & table.Mask)
            {
                var found = Volatile.Read(ref table.Keys[slot]);
                if (found == key)
                {
                    value = table.Values[slot];
                    return true;
                }
                if (found == 0) break;
            }
        }
        value = 0;
        return false;
    }

    // Remembers a pair's byte; a pair already remembered keeps its first byte, as every answer for a pair is the same.
    internal void Add(CliTypeIdentity first, CliTypeIdentity second, byte value)
    {
        var key = Key(Number(first), Number(second));
        var hash = Hash(key);
        var stripe = _stripes[StripeOf(hash)];
        lock (stripe.Gate)
        {
            var table = stripe.Table;
            if ((stripe.Count + 1) * 4L > table.Keys.Length * 3L)
            {
                table = Grown(table);
                Volatile.Write(ref stripe.Table, table);
            }
            var slot = SlotOf(hash, table);
            for (; table.Keys[slot] != 0; slot = (slot + 1) & table.Mask)
            {
                if (table.Keys[slot] == key) return;
            }
            table.Values[slot] = value;
            Volatile.Write(ref table.Keys[slot], key);
            Volatile.Write(ref stripe.Count, stripe.Count + 1);
        }
    }

    // Numbers start at 1, so a zero key is an empty slot.
    private int Number(CliTypeIdentity type) =>
        _numbers.TryGetValue(type, out var number)
            ? number
            : _numbers.GetOrAdd(type, static (_, memo) => Interlocked.Increment(ref memo._lastNumber), this);

    private static long Key(int first, int second) => ((long)first << 32) | (uint)second;

    // Fibonacci hashing: the product's high bits pick the stripe, the next ones the slot.
    private static ulong Hash(long key) => unchecked((ulong)key * 0x9E3779B97F4A7C15UL);

    private static int StripeOf(ulong hash) => (int)(hash >> (64 - StripeBits));

    private static int SlotOf(ulong hash, Table table) => (int)(hash >> 16) & table.Mask;

    // A table twice the size holding every pair, filled before any reader can see it.
    private static Table Grown(Table table)
    {
        var grown = new Table(table.Keys.Length * 2);
        for (var slot = 0; slot < table.Keys.Length; slot++)
        {
            var key = table.Keys[slot];
            if (key == 0) continue;
            var at = SlotOf(Hash(key), grown);
            while (grown.Keys[at] != 0) at = (at + 1) & grown.Mask;
            grown.Keys[at] = key;
            grown.Values[at] = table.Values[slot];
        }
        return grown;
    }

    private sealed class Stripe
    {
        internal readonly Lock Gate = new();
        internal Table Table = new(InitialCapacity);
        internal int Count;
    }

    private sealed class Table(int capacity)
    {
        internal readonly long[] Keys = new long[capacity];
        internal readonly byte[] Values = new byte[capacity];
        internal int Mask => Keys.Length - 1;
    }
}
