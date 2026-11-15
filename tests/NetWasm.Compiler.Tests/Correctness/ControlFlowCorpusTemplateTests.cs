// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ControlFlowCorpusTemplateTests
{
    [Fact]
    public void CreateRendersSelectedBranchLoopAndTransfer()
    {
        var subject = new ControlFlowCorpusTemplate();

        var fixture = subject.Create("ControlCase", Values(
            ("branch", "short-circuit"), ("loop", "do"), ("transfer", "continue")));

        Assert.Contains("do {", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("&&", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("continue;", fixture.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__", fixture.Source, StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
