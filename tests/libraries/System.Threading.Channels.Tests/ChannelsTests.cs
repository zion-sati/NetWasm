using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Threading.Channels.Tests;

public sealed class ChannelsTests
{
    [Test]
    public async Task UnboundedChannelKeepsWriteOrder()
    {
        var channel = Channel.CreateUnbounded<int>();
        for (var item = 1; item <= 3; item++)
        {
            await Assert.That(channel.Writer.TryWrite(item)).IsTrue();
        }

        await Assert.That(channel.Reader.CanCount).IsTrue();
        await Assert.That(channel.Reader.Count).IsEqualTo(3);
        await Assert.That(channel.Reader.TryPeek(out var first)).IsTrue();
        await Assert.That(first).IsEqualTo(1);
        channel.Writer.Complete();

        await Assert.That(await ReadAll(channel.Reader)).IsEqualTo("1,2,3");
        await Assert.That(channel.Reader.Completion.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task PendingReadCompletesWhenAnItemArrives()
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var read = channel.Reader.ReadAsync();
        await Assert.That(read.IsCompleted).IsFalse();

        await Assert.That(channel.Writer.TryWrite("item")).IsTrue();

        await Assert.That(await read).IsEqualTo("item");
    }

    [Test]
    public async Task WaitModeWriterWaitsForSpace()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
        await channel.Writer.WriteAsync(1);
        var second = channel.Writer.WriteAsync(2);
        await Assert.That(second.IsCompleted).IsFalse();
        await Assert.That(channel.Writer.TryWrite(3)).IsFalse();

        await Assert.That(await channel.Reader.ReadAsync()).IsEqualTo(1);
        await second;

        await Assert.That(await channel.Reader.ReadAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task DropOldestKeepsTheNewestItems() =>
        await AssertDrops(BoundedChannelFullMode.DropOldest, kept: "2,3", dropped: "1");

    [Test]
    public async Task DropNewestReplacesTheLastBufferedItem() =>
        await AssertDrops(BoundedChannelFullMode.DropNewest, kept: "1,3", dropped: "2");

    [Test]
    public async Task DropWriteDiscardsTheIncomingItem() =>
        await AssertDrops(BoundedChannelFullMode.DropWrite, kept: "1,2", dropped: "3");

    [Test]
    public async Task RendezvousWriteWaitsForAReader()
    {
        var channel = Channel.CreateBounded<int>(0);
        var write = channel.Writer.WriteAsync(7);
        await Assert.That(write.IsCompleted).IsFalse();

        await Assert.That(await channel.Reader.ReadAsync()).IsEqualTo(7);
        await write;
    }

    [Test]
    public async Task PrioritizedChannelReadsLowestFirst()
    {
        var channel = Channel.CreateUnboundedPrioritized<int>();
        foreach (var item in new[] { 5, 1, 4, 2, 3 })
        {
            await Assert.That(channel.Writer.TryWrite(item)).IsTrue();
        }
        channel.Writer.Complete();

        await Assert.That(await ReadAll(channel.Reader)).IsEqualTo("1,2,3,4,5");
    }

    [Test]
    public async Task CompletingWithAnErrorFaultsTheReader()
    {
        var channel = Channel.CreateUnbounded<int>();
        var error = new InvalidOperationException("producer failed");

        await Assert.That(channel.Writer.TryComplete(error)).IsTrue();
        await Assert.That(channel.Writer.TryComplete()).IsFalse();
        await Assert.That(channel.Writer.TryWrite(1)).IsFalse();

        ChannelClosedException? closed = null;
        try
        {
            await channel.Reader.ReadAsync();
        }
        catch (ChannelClosedException exception)
        {
            closed = exception;
        }
        await Assert.That(closed is not null && ReferenceEquals(closed.InnerException, error)).IsTrue();
        await Assert.That(channel.Reader.Completion.IsFaulted).IsTrue();
        await Assert.That(ReferenceEquals(channel.Reader.Completion.Exception!.InnerException, error)).IsTrue();
    }

    [Test]
    public async Task CompletedChannelStopsWaitingReaders()
    {
        var channel = Channel.CreateUnbounded<int>();
        var waiting = channel.Reader.WaitToReadAsync();
        await Assert.That(waiting.IsCompleted).IsFalse();

        channel.Writer.Complete();

        await Assert.That(await waiting).IsFalse();
        await Assert.That(channel.Reader.Completion.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task CanceledReadEndsWithoutTakingAnItem()
    {
        var channel = Channel.CreateUnbounded<int>();
        using var cancellation = new CancellationTokenSource();
        var read = channel.Reader.ReadAsync(cancellation.Token).AsTask();

        await cancellation.CancelAsync();
        var canceled = false;
        try
        {
            await read;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        await Assert.That(canceled).IsTrue();

        await Assert.That(channel.Writer.TryWrite(9)).IsTrue();
        await Assert.That(channel.Reader.TryRead(out var item)).IsTrue();
        await Assert.That(item).IsEqualTo(9);
    }

    [Test]
    public async Task ProducerAndConsumerInterleave()
    {
        var channel = Channel.CreateBounded<int>(2);
        var consumer = Sum(channel.Reader);
        for (var item = 1; item <= 100; item++)
        {
            await channel.Writer.WriteAsync(item);
        }
        channel.Writer.Complete();

        await Assert.That(await consumer).IsEqualTo(5050);
    }

    private static async Task AssertDrops(BoundedChannelFullMode mode, string kept, string dropped)
    {
        var droppedItems = new List<int>();
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2) { FullMode = mode }, droppedItems.Add);
        for (var item = 1; item <= 3; item++)
        {
            await Assert.That(channel.Writer.TryWrite(item)).IsTrue();
        }
        channel.Writer.Complete();

        await Assert.That(await ReadAll(channel.Reader)).IsEqualTo(kept);
        await Assert.That(string.Join(",", droppedItems)).IsEqualTo(dropped);
    }

    private static async Task<string> ReadAll(ChannelReader<int> reader)
    {
        var items = new List<int>();
        await foreach (var item in reader.ReadAllAsync())
        {
            items.Add(item);
        }

        return string.Join(",", items);
    }

    private static async Task<int> Sum(ChannelReader<int> reader)
    {
        var sum = 0;
        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(out var item))
            {
                sum += item;
            }
        }

        return sum;
    }
}
