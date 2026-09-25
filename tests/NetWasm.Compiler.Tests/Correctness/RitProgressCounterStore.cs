using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RitProgressCounter(long Version, string Key, string Line);

internal sealed class RitProgressCounterStore
{
    private readonly ConcurrentDictionary<string, RitProgressCounter> _counters =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<RitProgressCounter> _events = new();
    private long _version;

    public void Update(string key, string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        var version = Interlocked.Increment(ref _version);
        _counters[key] = new RitProgressCounter(version, key, line);
    }

    public void PublishEvent(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        var version = Interlocked.Increment(ref _version);
        _events.Enqueue(new RitProgressCounter(version, $"event:{version}", line));
    }

    public ImmutableArray<RitProgressCounter> SnapshotAfter(long version)
    {
        var pending = ImmutableArray.CreateBuilder<RitProgressCounter>();
        while (_events.TryDequeue(out var progressEvent))
        {
            if (progressEvent.Version > version)
            {
                pending.Add(progressEvent);
            }
        }

        pending.AddRange(_counters.Values.Where(counter => counter.Version > version));
        pending.Sort(static (left, right) => left.Version.CompareTo(right.Version));
        return pending.ToImmutable();
    }
}

internal sealed class RitProgressSnapshotWriter(
    RitProgressCounterStore counters,
    TextWriter output)
{
    private readonly Dictionary<string, long> _lastWrittenVersions =
        new(StringComparer.Ordinal);

    public RitProgressSnapshotWriter(RitProgressCounterStore counters)
        : this(counters, Console.Out)
    {
    }

    public void WritePending()
    {
        var snapshot = counters.SnapshotAfter(0);
        var wrote = false;
        foreach (var counter in snapshot)
        {
            if (_lastWrittenVersions.TryGetValue(counter.Key, out var writtenVersion) &&
                writtenVersion >= counter.Version)
            {
                continue;
            }

            output.WriteLine(counter.Line);
            _lastWrittenVersions[counter.Key] = counter.Version;
            wrote = true;
        }

        if (wrote)
        {
            output.Flush();
        }
    }
}
