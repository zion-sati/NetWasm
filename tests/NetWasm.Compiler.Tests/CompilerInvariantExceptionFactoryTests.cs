// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerInvariantExceptionFactoryTests
{
    [Fact]
    public void CreatesCompilerInvariantDiagnosticsWithOptionalLocation()
    {
        var factory = CreateFactory();

        var exception = factory.Create("invalid state", "Fixture.Run", 7);

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Equal("invalid state", exception.Diagnostic.Message);
        Assert.Equal("Fixture.Run", exception.Diagnostic.Method);
        Assert.Equal(7, exception.Diagnostic.IlOffset);
    }

    private static ICompilerInvariantExceptionFactory CreateFactory()
    {
        using var services = new ServiceCollection()
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<ICompilerInvariantExceptionFactory>();
    }
}
