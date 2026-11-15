// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ComplexityGrowthFixtureFactoryTests
{
    [Fact]
    public void GeneratesFiveDeterministicIncreasingMultiFamilyFixtures()
    {
        var factory = new ComplexityGrowthFixtureFactory();

        var first = factory.Create();
        var second = factory.Create();

        Assert.Equal([2, 4, 8, 16, 32], first.Select(item => item.Size));
        Assert.Equal(
            first.Select(item => item.Fixture.Source),
            second.Select(item => item.Fixture.Source));
        Assert.All(first, item =>
        {
            Assert.True(item.Fixture.CaptureCompilerDiagnostics);
            Assert.True(item.Fixture.RequiresReactor);
            Assert.Contains("Diamond", item.Fixture.Source, StringComparison.Ordinal);
            Assert.Contains("SwitchFanIn", item.Fixture.Source, StringComparison.Ordinal);
            Assert.Contains("NestedFinally", item.Fixture.Source, StringComparison.Ordinal);
            Assert.Contains("Irreducible", item.Fixture.Source, StringComparison.Ordinal);
            Assert.Contains("Task<int> Async", item.Fixture.Source, StringComparison.Ordinal);
            Assert.Contains('\n', item.Fixture.Source);
            Assert.DoesNotContain('\r', item.Fixture.Source);
        });
        Assert.True(first[^1].Fixture.Source.Length > first[0].Fixture.Source.Length * 4);
    }
}
