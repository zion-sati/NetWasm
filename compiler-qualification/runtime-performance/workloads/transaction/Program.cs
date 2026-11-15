// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Diagnostics;

internal static class Program
{
    private const int AccountCount = 2048;

    public static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] is not ("preallocated" or "full") ||
            !int.TryParse(args[1], out var count) || count <= 0 ||
            !uint.TryParse(args[2], out var seed) || seed == 0) return 2;
        if (args[0] == "full") return Full(count, seed);

        var transactions = CreateTransactions(count, seed);
        var normalized = new string[count];
        for (var i = 0; i < count; i++)
            normalized[i] = transactions[i].Account.Trim().ToUpperInvariant();
        var seen = new HashSet<int>(count);
        var balances = new Dictionary<string, decimal>(AccountCount, StringComparer.Ordinal);

        // Warm the exact path and containers. Do not pool equal strings: retain
        // the original workload's distinct per-transaction normalized values.
        var warm = Aggregate(transactions, normalized, seen, balances);
        var expected = Checksum(warm, balances);
        for (var warmup = 0; warmup < 8; warmup++)
        {
            seen.Clear();
            balances.Clear();
            warm = Aggregate(transactions, normalized, seen, balances);
        }
        seen.Clear();
        balances.Clear();
        _ = Stopwatch.GetTimestamp();
        _ = GC.CollectionCount(0);
        _ = GC.GetTotalAllocatedBytes(true);

        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        var counts = Aggregate(transactions, normalized, seen, balances);
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var collections = GC.CollectionCount(0) - collectionsBefore;

        // Checksum extraction and formatting may allocate, so run them only
        // after both measured counters have been captured.
        var checksum = Checksum(counts, balances);
        Console.WriteLine(string.Join("|", "PROBE", "preallocated",
            elapsed.ToString(), Stopwatch.Frequency.ToString(), checksum.ToString()));
        Console.WriteLine(string.Join("|", "GC", collections.ToString(),
            allocated.ToString(), GC.GetTotalMemory(false).ToString()));
        GC.KeepAlive(transactions);
        GC.KeepAlive(normalized);
        GC.KeepAlive(seen);
        GC.KeepAlive(balances);
        if (allocatedBefore <= 0) return 6;
        if (allocated != 0 || collections != 0) return 4;
        if (checksum != expected ||
            count == 100000 && seed == 101 && checksum != 377261514315152L) return 5;
        return 0;
    }

    // The full workload times everything the preallocated control moves out:
    // creating the transactions, normalizing every account and growing the
    // containers from empty, so allocation and collection are inside timing.
    private static int Full(int count, uint seed)
    {
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        var transactions = CreateTransactions(count, seed);
        var normalized = new string[count];
        for (var i = 0; i < count; i++)
            normalized[i] = transactions[i].Account.Trim().ToUpperInvariant();
        var seen = new HashSet<int>();
        var balances = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var counts = Aggregate(transactions, normalized, seen, balances);
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var collections = GC.CollectionCount(0) - collectionsBefore;

        var checksum = Checksum(counts, balances);
        Console.WriteLine(string.Join("|", "PROBE", "full",
            elapsed.ToString(), Stopwatch.Frequency.ToString(), checksum.ToString()));
        Console.WriteLine(string.Join("|", "GC", collections.ToString(),
            allocated.ToString(), GC.GetTotalMemory(false).ToString()));
        if (count == 100000 && seed == 101 && checksum != 377261514315152L) return 5;
        return 0;
    }

    private static Counts Aggregate(ProbeTransaction[] transactions, string[] normalized,
        HashSet<int> seen, Dictionary<string, decimal> balances)
    {
        var accepted = 0;
        var rejected = 0;
        var duplicates = 0;
        for (var index = 0; index < transactions.Length; index++)
        {
            var transaction = transactions[index];
            if (transaction.Cents <= 0 || transaction.Account.Length == 0)
            {
                rejected++;
                continue;
            }
            if (!seen.Add(transaction.Id))
            {
                duplicates++;
                continue;
            }
            var account = normalized[index];
            var amount = transaction.Cents / 100m;
            var fee = decimal.Round(amount * 0.0125m, 2, MidpointRounding.ToEven);
            balances.TryGetValue(account, out var balance);
            balances[account] = balance + amount - fee;
            accepted++;
        }
        return new(accepted, rejected, duplicates);
    }

    private static long Checksum(Counts counts, Dictionary<string, decimal> balances)
    {
        var checksum = ((long)counts.Accepted << 32) ^
            ((long)counts.Rejected << 16) ^ (uint)counts.Duplicates ^ balances.Count;
        foreach (var balance in balances.Values) checksum ^= decimal.GetBits(balance)[0];
        return checksum;
    }

    private static ProbeTransaction[] CreateTransactions(int count, uint seed)
    {
        var transactions = new ProbeTransaction[count];
        var random = seed;
        for (var index = 0; index < transactions.Length; index++)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            var account = " acct-" + (random % AccountCount).ToString() + " ";
            var cents = (int)(random % 100_000) + 1;
            if (index % 37 == 0) cents = -cents;
            transactions[index] = new(index > 0 && index % 10 == 0 ? index - 1 : index,
                account, cents);
        }
        return transactions;
    }

    private readonly record struct Counts(int Accepted, int Rejected, int Duplicates);

    private sealed class ProbeTransaction(int id, string account, int cents)
    {
        public readonly int IdField = id;
        public readonly string AccountField = account;
        public readonly int CentsField = cents;
        public int Id => IdField;
        public string Account => AccountField;
        public int Cents => CentsField;
    }
}
