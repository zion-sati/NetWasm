// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeOptimizationPolicyResolverTests
{
    [Theory]
    [InlineData(RuntimeWasmOptimization.None, null, false)]
    [InlineData(RuntimeWasmOptimization.O0, "-O0", false)]
    [InlineData(RuntimeWasmOptimization.O1, "-O1", true)]
    [InlineData(RuntimeWasmOptimization.O2, "-O2", true)]
    [InlineData(RuntimeWasmOptimization.O3, "-O3", true)]
    [InlineData(RuntimeWasmOptimization.Os, "-Os", false)]
    [InlineData(RuntimeWasmOptimization.Oz, "-Oz", false)]
    public void ResolvesOptimizerFlagAndNativeCapability(
        RuntimeWasmOptimization optimization,
        string? flag,
        bool preferNativeSpeed)
    {
        var resolver = Assert.IsAssignableFrom<IRuntimeOptimizationPolicyResolver>(
            new RuntimeOptimizationPolicyResolver());

        var policy = resolver.Resolve(optimization);

        Assert.Equal(flag, policy.BinaryenFlag);
        Assert.Equal(preferNativeSpeed, policy.PreferNativeSpeed);
        Assert.Same(policy, resolver.Resolve(optimization));
    }

    [Fact]
    public void RejectsUndefinedPreset()
    {
        var resolver = Assert.IsAssignableFrom<IRuntimeOptimizationPolicyResolver>(
            new RuntimeOptimizationPolicyResolver());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.Resolve((RuntimeWasmOptimization)42));

        Assert.Equal("optimization", exception.ParamName);
    }
}
