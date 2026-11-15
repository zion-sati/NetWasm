// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ValueCallCorpusTemplateTests
{
    [Fact]
    public void CreateRendersSelectedValueStorageAndCall()
    {
        var subject = new ValueCallCorpusTemplate();

        var fixture = subject.Create("ValueCase", Values(
            ("value", "struct"), ("storage", "array"), ("call", "delegate")));

        Assert.Contains("new Payload", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("new Payload[2]", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("delegate int Reader", fixture.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__", fixture.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateOmitsUnusedStaticStorage()
    {
        var subject = new ValueCallCorpusTemplate();

        var fixture = subject.Create("LocalValueCase", Values(
            ("value", "int"), ("storage", "local"), ("call", "direct")));

        Assert.DoesNotContain("_field", fixture.Source, StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
