// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Emission;

namespace NetWasm.Compiler.Tests;

public sealed class EmissionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddCompilerEmission());
    }

    [Fact]
    public void RegistrationReturnsTheOriginalServiceCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerEmission());
    }
}
