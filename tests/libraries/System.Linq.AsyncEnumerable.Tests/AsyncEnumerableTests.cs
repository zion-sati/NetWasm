using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Linq.AsyncEnumerable.Tests;

public sealed class AsyncEnumerableTests
{
    private static readonly int[] EarlyTerminationInput = [1, 2, 3, 4];
    private static readonly int[] OrderingInput = [3, 1, 2, 2];
    private static readonly int[] JoinOuterInput = [1, 2, 3];
    private static readonly int[] JoinInnerInput = [2, 3, 4];
    private static readonly int[] GenericNumberInput = [1, 2, 3];
    private static readonly string[] GenericWordInput = ["a", "bbb", "cc"];

    [Test]
    public async Task LazyPipelineStopsAfterTheRequestedElements()
    {
        var visited = 0;
        var values = await global::System.Linq.AsyncEnumerable.Range(1, 8)
            .Where(value =>
            {
                visited++;
                return value % 2 == 0;
            })
            .Select(value => value * 10)
            .Take(2)
            .ToArrayAsync();

        await Assert.That(values.Length).IsEqualTo(2);
        await Assert.That(values[0]).IsEqualTo(20);
        await Assert.That(values[1]).IsEqualTo(40);
        await Assert.That(visited).IsEqualTo(4);
    }

    [Test]
    public async Task SuspendedCallbacksPreserveValuesAndOrder()
    {
        var values = await global::System.Linq.AsyncEnumerable.Range(1, 5)
            .Where(IsOddAsync)
            .Select(DoubleAsync)
            .ToArrayAsync();

        await Assert.That(values.Length).IsEqualTo(3);
        await Assert.That(values[0]).IsEqualTo(2);
        await Assert.That(values[1]).IsEqualTo(6);
        await Assert.That(values[2]).IsEqualTo(10);
    }

    [Test]
    public async Task RepeatedEnumerationClonesPipelineState()
    {
        var pipeline = global::System.Linq.AsyncEnumerable.Range(1, 3)
            .Append(4)
            .Select(value => value * 10);

        var first = await pipeline.ToArrayAsync();
        var second = await pipeline.ToArrayAsync();

        await Assert.That(first.Length).IsEqualTo(4);
        await Assert.That(first[0]).IsEqualTo(10);
        await Assert.That(first[3]).IsEqualTo(40);
        await Assert.That(second.Length).IsEqualTo(4);
        await Assert.That(second[0]).IsEqualTo(10);
        await Assert.That(second[3]).IsEqualTo(40);
    }

    [Test]
    public async Task LookupGrowthPreservesEveryGroup()
    {
        var groups = await global::System.Linq.AsyncEnumerable.Range(0, 10)
            .GroupBy(value => value)
            .ToArrayAsync();

        await Assert.That(groups.Length).IsEqualTo(10);
        await Assert.That(groups[0].Key).IsEqualTo(0);
        await Assert.That(groups[0].Single()).IsEqualTo(0);
        await Assert.That(groups[9].Key).IsEqualTo(9);
        await Assert.That(groups[9].Single()).IsEqualTo(9);
    }

    [Test]
    public async Task EarlyTerminationDisposesTheSource()
    {
        var source = new TrackingAsyncEnumerable<int>(EarlyTerminationInput);

        var values = await source.Take(2).ToArrayAsync();

        await Assert.That(values.Length).IsEqualTo(2);
        await Assert.That(values[0]).IsEqualTo(1);
        await Assert.That(values[1]).IsEqualTo(2);
        await Assert.That(source.MoveNextCalls).IsEqualTo(2);
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task DelegateFaultAfterSuspensionIsPreserved()
    {
        Exception? observed = null;
        try
        {
            _ = await global::System.Linq.AsyncEnumerable.Range(1, 3)
                .Select(FaultAfterSuspensionAsync)
                .ToArrayAsync();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(observed is InvalidOperationException).IsTrue();
    }

    [Test]
    public async Task DisposalFaultIsPreserved()
    {
        Exception? observed = null;
        try
        {
            _ = await new FaultingDisposeAsyncEnumerable().ToArrayAsync();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(observed is InvalidOperationException).IsTrue();
    }

    [Test]
    public async Task CancellationAfterSuspensionDisposesTheEnumerator()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new CancelingAsyncEnumerable(cancellation);
        Exception? observed = null;
        try
        {
            _ = await source.ToArrayAsync(cancellation.Token);
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(observed is OperationCanceledException).IsTrue();
        await Assert.That(source.Disposed).IsTrue();
        await Assert.That(source.MoveNextCalls).IsEqualTo(1);
    }

    [Test]
    public async Task OrderingGroupingAndJoinRetainDeterministicSemantics()
    {
        var ordered = await OrderingInput
            .ToAsyncEnumerable()
            .Distinct()
            .Order()
            .ToArrayAsync();
        var groups = await global::System.Linq.AsyncEnumerable.Range(1, 4)
            .GroupBy(value => value % 2)
            .ToArrayAsync();
        var joined = await JoinOuterInput
            .ToAsyncEnumerable()
            .Join(
                JoinInnerInput.ToAsyncEnumerable(),
                outer => outer,
                inner => inner,
                (outer, inner) => outer * 10 + inner)
            .ToArrayAsync();

        await Assert.That(ordered.Length).IsEqualTo(3);
        await Assert.That(ordered[0]).IsEqualTo(1);
        await Assert.That(ordered[1]).IsEqualTo(2);
        await Assert.That(ordered[2]).IsEqualTo(3);
        await Assert.That(groups.Length).IsEqualTo(2);
        await Assert.That(groups[0].Key).IsEqualTo(1);
        await Assert.That(groups[0].Count()).IsEqualTo(2);
        await Assert.That(groups[1].Key).IsEqualTo(0);
        await Assert.That(groups[1].Count()).IsEqualTo(2);
        await Assert.That(joined.Length).IsEqualTo(2);
        await Assert.That(joined[0]).IsEqualTo(22);
        await Assert.That(joined[1]).IsEqualTo(33);
    }

    [Test]
    public async Task ValueAndReferenceGenericPipelinesRemainIndependent()
    {
        var numbers = await GenericNumberInput
            .ToAsyncEnumerable()
            .Select(value => value + 1)
            .ToArrayAsync();
        var words = await GenericWordInput
            .ToAsyncEnumerable()
            .Where(value => value.Length >= 2)
            .OrderBy(value => value.Length)
            .ToArrayAsync();

        await Assert.That(numbers.Length).IsEqualTo(3);
        await Assert.That(numbers[0]).IsEqualTo(2);
        await Assert.That(numbers[2]).IsEqualTo(4);
        await Assert.That(words.Length).IsEqualTo(2);
        await Assert.That(words[0]).IsEqualTo("cc");
        await Assert.That(words[1]).IsEqualTo("bbb");
    }

    [Test]
    public async Task DefaultStringOrderingIsDeterministic()
    {
        var strings = await new[] { "b", "a" }
            .ToAsyncEnumerable()
            .OrderBy(value => value)
            .ToArrayAsync();

        await Assert.That(strings.Length).IsEqualTo(2);
        await Assert.That(strings[0]).IsEqualTo("a");
        await Assert.That(strings[1]).IsEqualTo("b");
    }

    [Test]
    public async Task DefaultStringOrderingHandlesNonTrivialPartitions()
    {
        var input = new string[48];
        for (var index = 0; index < input.Length; index++)
        {
            input[index] = (input.Length - index - 1).ToString("D2");
        }

        var strings = await input
            .ToAsyncEnumerable()
            .OrderBy(value => value)
            .ToArrayAsync();

        await Assert.That(strings.Length).IsEqualTo(48);
        await Assert.That(strings[0]).IsEqualTo("00");
        await Assert.That(strings[47]).IsEqualTo("47");
    }

#if NETWASM
    [Test]
    public async Task SuspendedSecondaryKeysAreAwaitedBeforeSorting()
    {
        var secondaryKeys = await new[] { "a2", "a1" }
            .ToAsyncEnumerable()
            .OrderBy(value => value[0])
            .ThenBy(SecondaryKeyAfterSuspensionAsync)
            .ToArrayAsync();

        await Assert.That(secondaryKeys.Length).IsEqualTo(2);
        await Assert.That(secondaryKeys[0]).IsEqualTo("a1");
        await Assert.That(secondaryKeys[1]).IsEqualTo("a2");
    }
#endif

    private static async ValueTask<bool> IsOddAsync(
        int value,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return value % 2 != 0;
    }

    private static async ValueTask<int> DoubleAsync(
        int value,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return value * 2;
    }

#if NETWASM
    private static async ValueTask<char> SecondaryKeyAfterSuspensionAsync(
        string value,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return value[1];
    }
#endif

    private static async ValueTask<int> FaultAfterSuspensionAsync(
        int value,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException();
    }

    private sealed class TrackingAsyncEnumerable<T>(T[] values) :
        IAsyncEnumerable<T>,
        IAsyncEnumerator<T>
    {
        private readonly T[] _values = values;
        private int _index = -1;

        public int MoveNextCalls { get; private set; }

        public bool Disposed { get; private set; }

        public T Current => _values[_index];

        public IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            MoveNextCalls++;
            _index++;
            return new(_index < _values.Length);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }

    private sealed class FaultingDisposeAsyncEnumerable :
        IAsyncEnumerable<int>,
        IAsyncEnumerator<int>
    {
        private bool _moved;

        public int Current => 1;

        public IAsyncEnumerator<int> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            if (_moved)
            {
                return new(false);
            }

            _moved = true;
            return new(true);
        }

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException());
    }

    private sealed class CancelingAsyncEnumerable(
        CancellationTokenSource cancellation) :
        IAsyncEnumerable<int>,
        IAsyncEnumerator<int>
    {
        private CancellationToken _cancellationToken;

        public int Current => 0;

        public int MoveNextCalls { get; private set; }

        public bool Disposed { get; private set; }

        public IAsyncEnumerator<int> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            _cancellationToken = cancellationToken;
            return this;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            MoveNextCalls++;
            await Task.Yield();
            cancellation.Cancel();
            _cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
