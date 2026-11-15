// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AsyncCleanupCorpusTemplateTests
{
    [Fact]
    public void CreateRendersSelectedSuspensionCleanupAndOutcome()
    {
        var subject = new AsyncCleanupCorpusTemplate();

        var fixture = subject.Create("AsyncCase", Values(
            ("suspension", "pending"), ("cleanup", "async-dispose"),
            ("outcome", "failure"), ("gc", "none")));

        Assert.Contains("_pending.SetResult", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("await using", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException)", fixture.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__", fixture.Source, StringComparison.Ordinal);
        Assert.True(fixture.RequiresReactor);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
