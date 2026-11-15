// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Optimization;

public sealed class ManagedInliningHintsParserTests
{
    [Theory]
    [InlineData(null, ManagedInliningHints.Auto)]
    [InlineData("Auto", ManagedInliningHints.Auto)]
    [InlineData("IgnoreAggressive", ManagedInliningHints.IgnoreAggressive)]
    [InlineData("Preserve", ManagedInliningHints.Preserve)]
    public void ParsesCanonicalChoices(string? value, ManagedInliningHints expected)
    {
        var parser = Assert.IsAssignableFrom<IManagedInliningHintsParser>(new ManagedInliningHintsParser());
        var actual = parser.Parse(value);
        if (expected == ManagedInliningHints.Auto) Assert.Null(actual);
        else Assert.Equal(new(expected), actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("preserve")]
    [InlineData("1")]
    [InlineData("Preserve, IgnoreAggressive")]
    [InlineData("None")]
    public void RejectsUnknownOrNoncanonicalChoices(string value)
    {
        var parser = Assert.IsAssignableFrom<IManagedInliningHintsParser>(new ManagedInliningHintsParser());
        var error = Assert.Throws<CompilerException>(() => parser.Parse(value));
        Assert.Contains("Auto, IgnoreAggressive or Preserve", error.Diagnostic.Message, StringComparison.Ordinal);
    }
}
