// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GenericDispatchCorpusTemplateTests
{
    [Fact]
    public void CreateRendersSelectedSpecializationDispatchAndBoxing()
    {
        var subject = new GenericDispatchCorpusTemplate();

        var fixture = subject.Create("GenericCase", Values(
            ("specialization", "reference-value"), ("dispatch", "virtual"),
            ("boxing", "object")));

        Assert.Contains("new ReferencePair", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("object boxed", fixture.Source, StringComparison.Ordinal);
        Assert.Contains("new Holder<ReferencePair>", fixture.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__", fixture.Source, StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
}
