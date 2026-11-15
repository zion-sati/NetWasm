// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ValidCSharpFuzzGeneratorTests
{
    [Fact]
    public void GeneratesBoundedDeterministicLatestCSharpFromRecordedSeeds()
    {
        var generator = new ValidCSharpFuzzGenerator();

        var first = generator.Generate([3, 1, 2, 2]);
        var second = generator.Generate([2, 3, 1]);

        Assert.Equal(3, first.Length);
        Assert.Equal(
            first.Select(item => item.Fixture.Source),
            second.Select(item => item.Fixture.Source));
        Assert.All(first, item =>
        {
            Assert.Equal(4, item.Fixture.Inputs.Length);
            Assert.True(item.Fixture.Source.Length < 32 * 1024);
            Assert.Contains("unchecked", item.Fixture.Source);
        });
        Assert.Throws<ArgumentException>(() => generator.Generate([]));
    }
}
