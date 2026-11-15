// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class ControlFlowServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationRequiresAServiceCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ControlFlowServiceCollectionExtensions.AddCompilerControlFlow(null!));
        Assert.Throws<ArgumentNullException>(() =>
            StackTypeCompatibilityServiceCollectionExtensions
                .AddStackTypeCompatibility(null!));
    }

    [Fact]
    public void StackTypeCompatibilityCanBeComposedIndependently()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddStackTypeCompatibility());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<StackTypeCompatibilityValidator>(
            provider.GetRequiredService<IStackTypeCompatibilityValidator>());
    }

    [Fact]
    public void RegistrationExposesEachControlFlowFactory()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerControlFlow());

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IStackTypeCompatibilityValidator>());
        Assert.NotNull(provider.GetRequiredService<IReachableSetOverlapClassifier>());
        Assert.NotNull(provider.GetRequiredService<ITypedStackValidatorFactory>());
        Assert.NotNull(provider.GetRequiredService<IControlFlowGraphBuilderFactory>());
        Assert.NotNull(provider.GetRequiredService<IControlFlowGraphAnalyzerFactory>());
        Assert.NotNull(provider.GetRequiredService<IValidatedStructuredMethodBuilderFactory>());
    }
}
