// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InvalidCilReducerTests
{
    [Fact]
    public void ReducesToTheMinimalBytesThatPreserveTheNamedInvariant()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var original = Enumerable.Range(0, 128)
            .Select(index => unchecked((byte)index))
            .Append((byte)0xff)
            .Concat(Enumerable.Repeat((byte)0x42, 64))
            .ToArray();
        const string invariant = "unknown-opcode";

        var first = services.GetRequiredService<IInvalidCilReducer>().Reduce(new(
            original,
            invariant,
            (candidate, _) => candidate.Span.Contains((byte)0xff)
                ? invariant
                : null,
            TimeSpan.FromSeconds(5)));
        var second = services.GetRequiredService<IInvalidCilReducer>().Reduce(new(
            original,
            invariant,
            (candidate, _) => candidate.Span.Contains((byte)0xff)
                ? invariant
                : null,
            TimeSpan.FromSeconds(5)));

        Assert.False(first.TimedOut);
        Assert.Equal<byte>([0xff], first.Reduced);
        Assert.Equal(first.Reduced, second.Reduced);
        Assert.Equal(invariant, first.Invariant);
    }

    [Fact]
    public void ReturnsBestBytesAtTheDeadlineAndRejectsInvalidRequests()
    {
        var reducer = new InvalidCilReducer(new AdvancingTimeProvider());
        var result = reducer.Reduce(new(
            [1, 2, 3, 0xff],
            "named-invariant",
            (candidate, _) => candidate.Span.Contains((byte)0xff)
                ? "named-invariant"
                : null,
            TimeSpan.FromTicks(3)));

        Assert.True(result.TimedOut);
        Assert.Contains((byte)0xff, result.Reduced);
        Assert.Throws<ArgumentException>(() => reducer.Reduce(new(
            [],
            "named-invariant",
            (_, _) => "named-invariant",
            TimeSpan.FromSeconds(1))));
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Increment(ref _timestamp);
    }
}
