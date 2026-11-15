// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Validation.Delegates;

namespace NetWasm.Compiler.Tests;

public sealed class ValidationServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerValidation());
    }

    [Fact]
    public void RegistrationExposesEachValidationCapabilityThroughItsInterface()
    {
        var services = new ServiceCollection();
        services.AddCompilerControlFlow();
        services.AddCompilerGarbageCollection();
        services.AddSingleton<ILogger<CompilerComplexityInvariantValidator>>(
            NullLogger<CompilerComplexityInvariantValidator>.Instance);

        services.AddCompilerValidation();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IMetadataInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<IStructuredProgramInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<IReachabilityInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<IDelegateBindingInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<IRootMapInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<ILayoutInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<IWasmEmissionInvariantValidator>());
        Assert.NotNull(provider.GetRequiredService<ICompilerComplexityInvariantValidator>());
    }
}
