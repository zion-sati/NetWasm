using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Linq.Tests;

public sealed class EnumerableTests
{
    [Test]
    public async Task LazyFilteringProjectionAndTakeRemainDeferred()
    {
        var visited = 0;
        var projected = 0;
        var query = new[] { 1, 2, 3, 4, 5, 6, 7 }
            .Where(value =>
            {
                visited++;
                return value % 2 == 1;
            })
            .Select(value =>
            {
                projected++;
                return value * 10;
            })
            .Skip(1)
            .Take(2);

        await Assert.That(visited).IsEqualTo(0);
        await Assert.That(projected).IsEqualTo(0);

        var values = query.ToArray();

        await Assert.That(values.Length).IsEqualTo(2);
        await Assert.That(values[0]).IsEqualTo(30);
        await Assert.That(values[1]).IsEqualTo(50);
        await Assert.That(visited).IsEqualTo(5);
        await Assert.That(projected).IsEqualTo(3);
    }

    [Test]
    public async Task NestedLazyOperatorsAndFactoriesPreserveOrder()
    {
        var values = new object[] { 1, "skip", 2 }
            .OfType<int>()
            .SelectMany(value => new[] { value, value * 10 })
            .Concat(Array.Empty<int>())
            .DefaultIfEmpty(0)
            .Zip(new[] { 100, 200, 300, 400 }, (left, right) => left + right)
            .ToArray();

        await Assert.That(values.Length).IsEqualTo(4);
        await Assert.That(values[0]).IsEqualTo(101);
        await Assert.That(values[1]).IsEqualTo(210);
        await Assert.That(values[2]).IsEqualTo(302);
        await Assert.That(values[3]).IsEqualTo(420);

        var materialized = Enumerable.Repeat(9, 2)
            .Prepend(0)
            .Concat(new[] { 1, 2 })
            .Reverse()
            .ToList();
        var chunkSums = materialized.Chunk(2).Select(chunk => chunk.Sum()).ToArray();

        await Assert.That(materialized.Count).IsEqualTo(5);
        await Assert.That(materialized[0]).IsEqualTo(2);
        await Assert.That(materialized[4]).IsEqualTo(0);
        await Assert.That(chunkSums.Length).IsEqualTo(3);
        await Assert.That(chunkSums[0]).IsEqualTo(3);
        await Assert.That(chunkSums[1]).IsEqualTo(18);
        await Assert.That(chunkSums[2]).IsEqualTo(0);
    }

    [Test]
    public async Task ScalarTerminalsObserveExpectedContracts()
    {
        var values = new[] { 3, 5, 7 };

        await Assert.That(values.Any(value => value > 6)).IsTrue();
        await Assert.That(values.All(value => value > 0)).IsTrue();
        await Assert.That(values.Contains(5)).IsTrue();
        await Assert.That(values.First()).IsEqualTo(3);
        await Assert.That(values.Last()).IsEqualTo(7);
        await Assert.That(values.ElementAt(1)).IsEqualTo(5);
        await Assert.That(values.Single(value => value == 5)).IsEqualTo(5);
        await Assert.That(values.Count()).IsEqualTo(3);
        await Assert.That(values.Sum()).IsEqualTo(15);
        await Assert.That(values.Aggregate(0, (sum, value) => sum + value)).IsEqualTo(15);
        await Assert.That(Enumerable.Range(4, 3).Sum()).IsEqualTo(15);
    }

    [Test]
    public async Task EarlyTerminationDisposesTheSource()
    {
        var source = new TrackingEnumerable(1, 2, 3, 4);
        var values = source.Take(2).ToArray();

        await Assert.That(values.Length).IsEqualTo(2);
        await Assert.That(values[0]).IsEqualTo(1);
        await Assert.That(values[1]).IsEqualTo(2);
        await Assert.That(source.MoveNextCalls).IsEqualTo(2);
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task DelegateFaultDisposesTheSourceAndPreservesException()
    {
        var source = new TrackingEnumerable(1, 2, 3);
        Exception? observed = null;

        try
        {
            _ = source.Select(ThrowSelector).ToArray();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(observed is InvalidOperationException).IsTrue();
        await Assert.That(source.Disposed).IsTrue();
        await Assert.That(source.MoveNextCalls).IsEqualTo(1);
    }

    [Test]
    public async Task DisposalFaultIsObservableThroughMaterialization()
    {
        Exception? observed = null;
        var source = new DisposalFaultEnumerable();

        try
        {
            _ = source.Take(1).ToArray();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(observed is InvalidOperationException).IsTrue();
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task ComparersSetsGroupingJoinsAndOrderingRemainStable()
    {
        var comparer = new CollisionComparer();
        var distinct = new[] { 1, 2, 1, 3, 2 }.Distinct(comparer).ToArray();
        var except = new[] { 1, 2, 3, 4 }.Except(new[] { 2, 4 }, comparer).ToArray();
        var grouped = new[] { 1, 2, 3, 4, 5 }
            .GroupBy(value => value % 2)
            .Select(group => group.Key * 10 + group.Count())
            .ToArray();
        var ordered = new[] { 3, 1, 2, 2 }
            .OrderBy(value => value, new NumericComparer())
            .ThenByDescending(value => value)
            .ToArray();
        var joined = new[] { 1, 2, 3 }
            .Join(new[] { 2, 3, 4 }, value => value, value => value, (left, right) => left * right)
            .ToArray();
        var groupJoined = new[] { 1, 2, 3 }
            .GroupJoin(
                new[] { 2, 2, 3 },
                value => value,
                value => value,
                (outer, matches) => outer * 10 + matches.Count())
            .ToArray();

        await Assert.That(distinct.Length).IsEqualTo(3);
        await Assert.That(distinct[0]).IsEqualTo(1);
        await Assert.That(distinct[2]).IsEqualTo(3);
        await Assert.That(except.Length).IsEqualTo(2);
        await Assert.That(except[0]).IsEqualTo(1);
        await Assert.That(except[1]).IsEqualTo(3);
        await Assert.That(grouped.Length).IsEqualTo(2);
        await Assert.That(grouped[0]).IsEqualTo(13);
        await Assert.That(grouped[1]).IsEqualTo(2);
        await Assert.That(ordered[0]).IsEqualTo(1);
        await Assert.That(ordered[3]).IsEqualTo(3);
        await Assert.That(joined.Length).IsEqualTo(2);
        await Assert.That(joined[0]).IsEqualTo(4);
        await Assert.That(joined[1]).IsEqualTo(9);
        await Assert.That(groupJoined[0]).IsEqualTo(10);
        await Assert.That(groupJoined[1]).IsEqualTo(22);
        await Assert.That(groupJoined[2]).IsEqualTo(31);
    }

    [Test]
    public async Task MaterializationSupportsCollectionsAndReportsDuplicateKeys()
    {
        var list = new[] { 1, 2, 2, 3 }.ToList();
        var set = list.ToHashSet(new CollisionComparer());
        var lookup = list.ToLookup(value => value % 2);
        var dictionary = new[] { "one", "four" }
            .ToDictionary(value => value.Length, value => value);
        Exception? observed = null;

        try
        {
            _ = new[] { 1, 1 }.ToDictionary(value => value);
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        await Assert.That(list.Count).IsEqualTo(4);
        await Assert.That(set.Count).IsEqualTo(3);
        await Assert.That(lookup[1].Count()).IsEqualTo(2);
        await Assert.That(lookup[0].Count()).IsEqualTo(2);
        await Assert.That(dictionary[3]).IsEqualTo("one");
        await Assert.That(dictionary[4]).IsEqualTo("four");
        await Assert.That(observed is ArgumentException).IsTrue();
    }

    [Test]
    public async Task ValueAndReferenceGenericPipelinesRemainIndependent()
    {
        var coordinates = new[] { new Coordinate(1, 2), new Coordinate(3, 4) };
        var samples = new[]
        {
            new Sample("a", 1),
            new Sample("bbb", 3),
            new Sample("cc", 2),
        };

        var coordinateTotal = SumValues(coordinates, coordinate => coordinate.Left + coordinate.Right);
        var names = samples
            .Where(sample => sample.Value >= 2)
            .OrderBy(sample => sample.Name.Length)
            .Select(sample => sample.Name)
            .ToArray();

        await Assert.That(coordinateTotal).IsEqualTo(10);
        await Assert.That(names.Length).IsEqualTo(2);
        await Assert.That(names[0]).IsEqualTo("cc");
        await Assert.That(names[1]).IsEqualTo("bbb");
    }

    [Test]
    public async Task ImmutableArrayExtensionsComposeAndAggregateValues()
    {
        var values = ImmutableArray.Create(1, 2, 3);
        var empty = ImmutableArray<int>.Empty;
        var projected = values.Select(value => value * 2).ToArray();
        var flattened = values
            .SelectMany(value => new[] { value, value + 10 })
            .ToArray();
        var composed = values
            .SelectMany(value => new[] { value, value + 10 }, (value, item) => value + item)
            .ToArray();
        var emptyOuter = empty
            .SelectMany(value => new[] { value }, (value, item) => value + item)
            .ToArray();
        var emptyInner = values
            .SelectMany(value => Array.Empty<int>(), (value, item) => value + item)
            .ToArray();
        var filtered = values.Where(value => value > 1).ToArray();
        var sum = values.Aggregate((left, right) => left + right);
        var seededSum = values.Aggregate(10, (total, value) => total + value);
        var selectedSum = values.Aggregate(10, (total, value) => total + value, total => $"sum={total}");
        var clone = values.ToArray();
        clone[0] = 99;

        await Assert.That(projected.Length).IsEqualTo(3);
        await Assert.That(projected[0]).IsEqualTo(2);
        await Assert.That(projected[1]).IsEqualTo(4);
        await Assert.That(projected[2]).IsEqualTo(6);
        await Assert.That(flattened.Length).IsEqualTo(6);
        await Assert.That(flattened[0]).IsEqualTo(1);
        await Assert.That(flattened[1]).IsEqualTo(11);
        await Assert.That(flattened[2]).IsEqualTo(2);
        await Assert.That(flattened[3]).IsEqualTo(12);
        await Assert.That(flattened[4]).IsEqualTo(3);
        await Assert.That(flattened[5]).IsEqualTo(13);
        await Assert.That(composed.Length).IsEqualTo(6);
        await Assert.That(composed[0]).IsEqualTo(2);
        await Assert.That(composed[1]).IsEqualTo(12);
        await Assert.That(composed[5]).IsEqualTo(16);
        await Assert.That(emptyOuter.Length).IsEqualTo(0);
        await Assert.That(emptyInner.Length).IsEqualTo(0);
        await Assert.That(filtered.Length).IsEqualTo(2);
        await Assert.That(filtered[0]).IsEqualTo(2);
        await Assert.That(filtered[1]).IsEqualTo(3);
        await Assert.That(values.Any()).IsTrue();
        await Assert.That(empty.Any()).IsFalse();
        await Assert.That(values.Any(value => value == 2)).IsTrue();
        await Assert.That(values.Any(value => value > 9)).IsFalse();
        await Assert.That(values.All(value => value > 0)).IsTrue();
        await Assert.That(values.All(value => value < 3)).IsFalse();
        await Assert.That(values.SequenceEqual(ImmutableArray.Create(1, 2, 3))).IsTrue();
        await Assert.That(values.SequenceEqual(values.Select(value => value), EqualityComparer<int>.Default)).IsTrue();
        await Assert.That(values.SequenceEqual(values)).IsTrue();
        await Assert.That(values.SequenceEqual(ImmutableArray.Create(1, 2))).IsFalse();
        await Assert.That(values.SequenceEqual(ImmutableArray.Create(1, 4, 3))).IsFalse();
        await Assert.That(sum).IsEqualTo(6);
        await Assert.That(seededSum).IsEqualTo(16);
        await Assert.That(empty.Aggregate(10, (total, value) => total + value)).IsEqualTo(10);
        await Assert.That(selectedSum).IsEqualTo("sum=16");
        await Assert.That(values.ElementAt(1)).IsEqualTo(2);
        await Assert.That(values.ElementAtOrDefault(1)).IsEqualTo(2);
        await Assert.That(values.ElementAtOrDefault(-1)).IsEqualTo(0);
        await Assert.That(values.ElementAtOrDefault(9)).IsEqualTo(0);
        await Assert.That(values.First()).IsEqualTo(1);
        await Assert.That(values.First(value => value == 2)).IsEqualTo(2);
        await Assert.That(values.FirstOrDefault()).IsEqualTo(1);
        await Assert.That(values.FirstOrDefault(value => value == 2)).IsEqualTo(2);
        await Assert.That(values.FirstOrDefault(value => value > 9)).IsEqualTo(0);
        await Assert.That(values.Last()).IsEqualTo(3);
        await Assert.That(values.Last(value => value < 3)).IsEqualTo(2);
        await Assert.That(values.LastOrDefault()).IsEqualTo(3);
        await Assert.That(values.LastOrDefault(value => value == 2)).IsEqualTo(2);
        await Assert.That(values.LastOrDefault(value => value > 9)).IsEqualTo(0);
        await Assert.That(values.Single(value => value == 2)).IsEqualTo(2);
        await Assert.That(values.SingleOrDefault(value => value == 2)).IsEqualTo(2);
        await Assert.That(empty.SingleOrDefault()).IsEqualTo(0);
        await Assert.That(CaptureException(() => empty.First()) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.First(value => value > 9)) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => empty.Last()) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.Last(value => value > 9)) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.Single()) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => empty.Single()) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.Single(value => value > 9)) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.Single(value => value > 1)) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.SingleOrDefault()) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => values.SingleOrDefault(value => value > 1)) is InvalidOperationException).IsTrue();
        await Assert.That(clone[0]).IsEqualTo(99);
        await Assert.That(values[0]).IsEqualTo(1);
        await Assert.That(empty.ToArray().Length).IsEqualTo(0);
    }

    [Test]
    public async Task ImmutableArrayExtensionsPreserveReferenceAndFailureContracts()
    {
        var values = ImmutableArray.Create("a", "b", "c");
        var derived = ImmutableArray.Create(new NamedValue("a"), new NamedValue("b"));
        var baseValues = ImmutableArray.Create<INamedValue>(derived[0], derived[1]);
        var comparer = new NamedValueComparer();
        var empty = ImmutableArray<string>.Empty;
        var one = ImmutableArray.Create(2);
        Exception? firstFailure = null;

        try
        {
            _ = empty.First();
        }
        catch (InvalidOperationException exception)
        {
            firstFailure = exception;
        }

        await Assert.That(values.SequenceEqual(
            ImmutableArray.Create("A", "B", "C"),
            comparer)).IsTrue();
        await Assert.That(baseValues.SequenceEqual(derived)).IsTrue();
        await Assert.That(baseValues.SequenceEqual(baseValues)).IsTrue();
        await Assert.That(baseValues.SequenceEqual(ImmutableArray.Create<INamedValue>(derived[0]))).IsFalse();
        await Assert.That(baseValues.SequenceEqual(
            (IEnumerable<NamedValue>)derived)).IsTrue();
        await Assert.That(baseValues.SequenceEqual(
            (IEnumerable<NamedValue>)new[] { derived[0] })).IsFalse();
        await Assert.That(baseValues.SequenceEqual(
            (IEnumerable<NamedValue>)new[] { derived[0], derived[1], derived[0] })).IsFalse();
        await Assert.That(baseValues.SequenceEqual(
            derived,
            (left, right) => left.Name == right.Name)).IsTrue();
        await Assert.That(baseValues.SequenceEqual(
            baseValues,
            (left, right) => left.Name == right.Name)).IsTrue();
        await Assert.That(baseValues.SequenceEqual(
            ImmutableArray.Create<INamedValue>(derived[0]),
            (left, right) => left.Name == right.Name)).IsFalse();
        await Assert.That(baseValues.SequenceEqual(
            ImmutableArray.Create<INamedValue>(derived[0], derived[1]),
            (left, right) => left.Name != right.Name)).IsFalse();
        await Assert.That(empty.FirstOrDefault()).IsNull();
        await Assert.That(empty.LastOrDefault()).IsNull();
        await Assert.That(empty.Aggregate((left, right) => left + right)).IsNull();
        await Assert.That(firstFailure is InvalidOperationException).IsTrue();

        var byValue = ImmutableArray.Create(1, 2).ToDictionary(value => value);
        var byElement = ImmutableArray.Create(1, 2)
            .ToDictionary(value => value, value => $"n{value}");
        var byComparer = ImmutableArray.Create(1, 2)
            .ToDictionary(value => value, EqualityComparer<int>.Default);
        var byElementAndComparer = ImmutableArray.Create(1, 2)
            .ToDictionary(value => value, value => value * 10, EqualityComparer<int>.Default);

        await Assert.That(byValue[1]).IsEqualTo(1);
        await Assert.That(byElement[2]).IsEqualTo("n2");
        await Assert.That(byComparer[1]).IsEqualTo(1);
        await Assert.That(byElementAndComparer[2]).IsEqualTo(20);
        await Assert.That(CaptureException(() => ImmutableArray.Create(1, 1).ToDictionary(value => value))
            is ArgumentException).IsTrue();

        var builder = ImmutableArray.CreateBuilder<int>();
        builder.Add(4);
        builder.Add(5);
        var emptyBuilder = ImmutableArray.CreateBuilder<int>();

        await Assert.That(builder.First()).IsEqualTo(4);
        await Assert.That(builder.FirstOrDefault()).IsEqualTo(4);
        await Assert.That(builder.Last()).IsEqualTo(5);
        await Assert.That(builder.LastOrDefault()).IsEqualTo(5);
        await Assert.That(builder.Any()).IsTrue();
        await Assert.That(emptyBuilder.FirstOrDefault()).IsEqualTo(0);
        await Assert.That(emptyBuilder.LastOrDefault()).IsEqualTo(0);
        await Assert.That(emptyBuilder.Any()).IsFalse();
        await Assert.That(CaptureException(() => emptyBuilder.First()) is InvalidOperationException).IsTrue();
        await Assert.That(CaptureException(() => emptyBuilder.Last()) is InvalidOperationException).IsTrue();
        await Assert.That(one.Single()).IsEqualTo(2);
        await Assert.That(one.SingleOrDefault()).IsEqualTo(2);
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static int ThrowSelector(int value) => throw new InvalidOperationException();

    private static int SumValues<T>(IEnumerable<T> values, Func<T, int> selector) =>
        values.Select(selector).Sum();

    private sealed class TrackingEnumerable(params int[] values) : IEnumerable<int>, IEnumerator<int>
    {
        private readonly int[] _values = values;
        private int _index = -1;

        public int MoveNextCalls { get; private set; }

        public bool Disposed { get; private set; }

        public int Current => _values[_index];

        object IEnumerator.Current => Current;

        public IEnumerator<int> GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool MoveNext()
        {
            MoveNextCalls++;
            _index++;
            return _index < _values.Length;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    private sealed class DisposalFaultEnumerable : IEnumerable<int>, IEnumerator<int>
    {
        private bool _moved;

        public bool Disposed { get; private set; }

        public int Current => 1;

        object IEnumerator.Current => Current;

        public IEnumerator<int> GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool MoveNext()
        {
            if (_moved)
            {
                return false;
            }

            _moved = true;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
            Disposed = true;
            throw new InvalidOperationException();
        }
    }

    private sealed class CollisionComparer : IEqualityComparer<int>
    {
        public bool Equals(int left, int right) => left == right;

        public int GetHashCode(int value) => 1;
    }

    private sealed class NumericComparer : IComparer<int>
    {
        public int Compare(int left, int right) => left.CompareTo(right);
    }

    private readonly struct Coordinate
    {
        public Coordinate(int left, int right)
        {
            Left = left;
            Right = right;
        }

        public int Left { get; }

        public int Right { get; }
    }

    private sealed class Sample(string name, int value)
    {
        public string Name { get; } = name;

        public int Value { get; } = value;
    }

    private interface INamedValue
    {
        string Name { get; }
    }

    private sealed class NamedValue(string name) : INamedValue
    {
        public string Name { get; } = name;
    }

    private sealed class NamedValueComparer : IEqualityComparer<string>
    {
        public bool Equals(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string value) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }
}
