// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class SourceFailureReducerTests
{
    [Fact]
    public void RemovesSyntaxWhilePreservingTheNamedFailurePredicate()
    {
        var reducer = new SourceFailureReducer(new FrozenTimeProvider());
        const string marker = "throw new System.Exception();";
        var result = reducer.Reduce(new(
            $$"""
            namespace ReducerFixture;
            public static class EntryPoint
            {
                public static int Unused() { return 7; }
                public static void Run()
                {
                    var unused = 1;
                    {{marker}}
                }
            }
            """,
            source => source.Contains(marker, StringComparison.Ordinal),
            TimeSpan.FromSeconds(2)));

        Assert.True(result.Changed);
        Assert.True(result.Completed);
        Assert.True(result.Attempts > 0);
        Assert.Contains(marker, result.Reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Unused", result.Reduced, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidInputAndNonPositiveDeadline()
    {
        var reducer = new SourceFailureReducer(TimeProvider.System);

        Assert.Throws<ArgumentException>(() => reducer.Reduce(new(
            "not C# {{{",
            _ => true,
            TimeSpan.FromSeconds(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => reducer.Reduce(new(
            "class C { }",
            _ => true,
            TimeSpan.Zero)));
    }

    private sealed class FrozenTimeProvider : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }
}
