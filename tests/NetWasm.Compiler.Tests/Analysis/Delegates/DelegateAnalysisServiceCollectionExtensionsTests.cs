// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests.Analysis.Delegates;

public sealed class DelegateAnalysisServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationExposesTheDelegateBindingFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IManagedMethodIdentityFactory,
            ManagedMethodIdentityFactory>();
        services.AddSingleton<ILogger<DiagnosticDelegateBindingPlanner>>(
            NullLogger<DiagnosticDelegateBindingPlanner>.Instance);

        services.AddDelegateAnalysis();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DelegateBindingPlannerFactory>(
            provider.GetRequiredService<IDelegateBindingPlannerFactory>());
    }

    [Fact]
    public void RegistrationRejectsMissingServices()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DelegateAnalysisServiceCollectionExtensions.AddDelegateAnalysis(null!));
    }
}
