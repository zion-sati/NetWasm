// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CfgPropertyCorpusGeneratorTests
{
    [Fact]
    public void GeneratesDeterministicReducibleAndIrreducibleGraphs()
    {
        var generator = new CfgPropertyCorpusGenerator();

        var first = generator.Generate([7, 3]);
        var second = generator.Generate([3, 7]);

        Assert.Equal(4, first.Length);
        Assert.Equal(first.Select(item => item.Name), second.Select(item => item.Name));
        Assert.Equal(first.Select(item => item.Fixture.Source),
            second.Select(item => item.Fixture.Source));
        Assert.Contains(first, item => item.Shape == "Reducible");
        Assert.Contains(first, item => item.Shape == "Irreducible");
        Assert.All(first, item => Assert.Equal(4, item.Expected.Count));
        Assert.Throws<ArgumentException>(() => generator.Generate([]));
    }
}
