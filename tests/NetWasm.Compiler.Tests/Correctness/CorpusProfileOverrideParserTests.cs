// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusProfileOverrideParserTests
{
    [Fact]
    public void AbsentOverrideKeepsTheManifestDefault() => Assert.Null(Parser().Parse(null));

    [Theory]
    [InlineData("Fast", 0)]
    [InlineData("fast", 0)]
    [InlineData("Family", 1)]
    [InlineData("FAMILY", 1)]
    [InlineData("Extended", 2)]
    [InlineData("extended", 2)]
    public void AcceptsOnlyNamedProfiles(string name, int profile) =>
        Assert.Equal((CorpusMatrixProfile)profile, Parser().Parse(name));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Fast ")]
    [InlineData(" Fast")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("99")]
    [InlineData("Unknown")]
    [InlineData("Fast, Family")]
    [InlineData("Fast|Extended")]
    public void InvalidOverrideNeverFallsBackToASmallerMatrix(string name) =>
        Assert.Throws<ArgumentException>(() => Parser().Parse(name));

    private static ICorpusProfileOverrideParser Parser() =>
        Assert.IsAssignableFrom<ICorpusProfileOverrideParser>(new CorpusProfileOverrideParser());
}
