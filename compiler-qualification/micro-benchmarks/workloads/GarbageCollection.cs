using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;

namespace NetWasm.Workloads
{
    // The timed regions of NetWasm's allocation and collection workloads
    // (compiler-qualification/runtime-performance/workloads, transaction and gc), at the
    // public table's sizes: full 100000 101, object-graph 100000 and ui-churn 500. Their
    // programs run once in a fresh process; here each call starts from whatever the previous
    // calls left on the heap.
    public class GarbageCollection
    {
        private const int AccountCount = 2048;

        // Creates 100,000 transactions, normalizes every account and grows the containers
        // from empty; 377261514315152 at this size and seed.
        [Benchmark]
        public long FullTransaction()
        {
            const int count = 100_000;
            var transactions = CreateTransactions(count, 101);
            var normalized = new string[count];
            for (var i = 0; i < count; i++)
                normalized[i] = transactions[i].Account.Trim().ToUpperInvariant();
            var seen = new HashSet<int>();
            var balances = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var counts = Aggregate(transactions, normalized, seen, balances);
            return Checksum(counts, balances);
        }

        // Builds a graph of 100,000 small objects that stays alive to the end.
        [Benchmark]
        public long RetainedObjectGraph()
        {
            const int count = 100_000;
            var objects = new ProbeObject[count];
            var random = 1U;
            for (var index = 0; index < count; index++)
            {
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                objects[index] = new ProbeObject(
                    " acct-" + (random % 2048).ToString() + " ",
                    (int)(random % 100_000) + 1);
            }
            return objects.Length + objects[count - 1].Cents + objects[count - 1].Account.Length;
        }

        // Keeps 2,048 objects alive and replaces 64 of them from 512 new ones a frame.
        [Benchmark]
        public long UiChurn()
        {
            const int frames = 500;
            var retained = new ProbeObject[2048];
            for (var index = 0; index < retained.Length; index++)
                retained[index] = new ProbeObject("node-" + index.ToString(), index);
            var checksum = 0L;
            for (var frame = 0; frame < frames; frame++)
            {
                var transient = new ProbeObject[512];
                for (var index = 0; index < transient.Length; index++)
                {
                    var identity = (frame * 31 + index) & 2047;
                    transient[index] = new ProbeObject(
                        "node-" + identity.ToString(), frame + index);
                }
                for (var index = 0; index < 64; index++)
                    retained[(frame * 67 + index) & 2047] = transient[index];
                checksum += transient[^1].Cents + retained[frame & 2047].Account.Length;
            }
            return checksum;
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

        private sealed class ProbeObject(string account, int cents)
        {
            public string Account { get; } = account;
            public int Cents { get; } = cents;
        }
    }
}
