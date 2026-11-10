using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

// Timed loops retained from performance-residual-current/consumer/Program.cs.
// Full graph validation and explicit collections are outside those timings.
internal static class Program
{
    private sealed class ProbeObject(string account, int cents)
    {
        public string Account { get; } = account;
        public int Cents { get; } = cents;
    }

    private readonly record struct Result(
        string Name, long ElapsedTicks, long Checksum, long AllocatedBytes,
        int AutomaticCollections, long RetainedBytes, int RetainedModels);

    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0)
            return 2;
        var result = args[0] switch
        {
            "object-graph" => ObjectGraph(count),
            "ui-churn" => UiChurn(count),
            "binary-trees" => BinaryTrees(count),
            "nullable-boxes" => NullableBoxes(count),
            _ => default,
        };
        if (result.Name is null) return 3;
        Console.WriteLine(string.Join("|", "GCWORK", result.Name,
            result.ElapsedTicks.ToString(), Stopwatch.Frequency.ToString(), result.Checksum.ToString(),
            result.AllocatedBytes.ToString(), result.AutomaticCollections.ToString(),
            result.RetainedBytes.ToString(), result.RetainedModels.ToString()));
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Result ObjectGraph(int count)
    {
        var objects = new ProbeObject[count];
        var random = 1U;
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < count; index++)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            objects[index] = new ProbeObject(
                " acct-" + (random % 2048).ToString() + " ",
                (int)(random % 100_000) + 1);
        }
        var checksum = objects.Length + objects[count - 1].Cents + objects[count - 1].Account.Length;
        var elapsed = Stopwatch.GetTimestamp() - started;
        var automatic = GC.CollectionCount(0) - collectionsBefore;
        var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        GC.Collect();
        var retainedBytes = GC.GetTotalMemory(false);
        ValidateGraph(objects);
        GC.KeepAlive(objects);
        return new Result("object-graph", elapsed, checksum, allocated, automatic, retainedBytes, count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Result UiChurn(int frames)
    {
        var retained = new ProbeObject[2048];
        for (var index = 0; index < retained.Length; index++)
            retained[index] = new ProbeObject("node-" + index.ToString(), index);
        var checksum = 0L;
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
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
        var elapsed = Stopwatch.GetTimestamp() - started;
        var automatic = GC.CollectionCount(0) - collectionsBefore;
        var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        GC.Collect();
        var retainedBytes = GC.GetTotalMemory(false);
        ValidateUi(retained, frames);
        GC.KeepAlive(retained);
        return new Result("ui-churn", elapsed, checksum, allocated, automatic, retainedBytes, retained.Length);
    }

    private sealed class TreeNode(TreeNode? left, TreeNode? right)
    {
        public TreeNode? Left { get; } = left;
        public TreeNode? Right { get; } = right;
    }

    // The Computer Language Benchmarks Game's binary-trees: short-lived complete
    // trees of every even depth from 4, next to one tree that lives throughout.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Result BinaryTrees(int maxDepth)
    {
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
        var longLived = Tree(maxDepth);
        var checksum = 0L;
        for (var depth = 4; depth <= maxDepth; depth += 2)
        {
            var iterations = 1 << (maxDepth - depth + 4);
            for (var index = 0; index < iterations; index++)
                checksum += Count(Tree(depth));
        }
        checksum += Count(longLived);
        var elapsed = Stopwatch.GetTimestamp() - started;
        var automatic = GC.CollectionCount(0) - collectionsBefore;
        var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        GC.Collect();
        var retainedBytes = GC.GetTotalMemory(false);
        var retained = Count(longLived);
        GC.KeepAlive(longLived);
        return new Result("binary-trees", elapsed, checksum, allocated, automatic, retainedBytes, retained);
    }

    // Boxes a Nullable<int> each iteration into a ring of retained slots, a null
    // one in eight, and reads an older box back.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Result NullableBoxes(int count)
    {
        var boxes = new object?[1024];
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
        var checksum = 0L;
        for (var index = 0; index < count; index++)
        {
            int? value = (index & 7) == 0 ? null : index;
            boxes[index & 1023] = value;
            if (boxes[(index * 7) & 1023] is int boxed) checksum += boxed;
        }
        var elapsed = Stopwatch.GetTimestamp() - started;
        var automatic = GC.CollectionCount(0) - collectionsBefore;
        var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        GC.Collect();
        var retainedBytes = GC.GetTotalMemory(false);
        GC.KeepAlive(boxes);
        return new Result("nullable-boxes", elapsed, checksum, allocated, automatic, retainedBytes, boxes.Length);
    }

    private static TreeNode Tree(int depth) =>
        depth == 0 ? new TreeNode(null, null) : new TreeNode(Tree(depth - 1), Tree(depth - 1));

    private static int Count(TreeNode node) =>
        node.Left is null ? 1 : 1 + Count(node.Left) + Count(node.Right!);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateGraph(ProbeObject[] objects)
    {
        var random = 1U;
        foreach (var value in objects)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            if (value is null || value.Cents != (int)(random % 100_000) + 1 ||
                value.Account != " acct-" + (random % 2048).ToString() + " ")
                throw new InvalidOperationException("Retained graph validation failed.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateUi(ProbeObject[] retained, int frames)
    {
        var identities = new int[retained.Length];
        var cents = new int[retained.Length];
        for (var index = 0; index < retained.Length; index++)
            identities[index] = cents[index] = index;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var index = 0; index < 64; index++)
            {
                var slot = (frame * 67 + index) & 2047;
                identities[slot] = (frame * 31 + index) & 2047;
                cents[slot] = frame + index;
            }
        }
        for (var index = 0; index < retained.Length; index++)
        {
            var value = retained[index];
            if (value is null || value.Cents != cents[index] ||
                value.Account != "node-" + identities[index].ToString())
                throw new InvalidOperationException("Retained UI validation failed.");
        }
    }
}

