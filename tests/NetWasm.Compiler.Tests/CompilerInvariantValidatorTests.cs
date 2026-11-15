// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerInvariantValidatorTests
{
    [Fact]
    public void LayoutValidatorAcceptsTheRequestedTargetAndRejectsAnotherTarget()
    {
        var validator = CreateValidator();

        AssertContract(validator, new TestTargetLayout(WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!, WasmTarget.Wasm32));
    }

    private static void AssertContract<TValidator>(
        TValidator validator,
        ITargetLayout layouts)
        where TValidator : ILayoutInvariantValidator
    {
        validator.Validate(layouts, WasmTarget.Wasm32);
        var error = Assert.Throws<CompilerException>(() =>
            validator.Validate(layouts, WasmTarget.Wasm64));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
    }

    private sealed class TestTargetLayout(WasmTarget target) : ITargetLayout
    {
        public WasmTargetLayout Target { get; } = WasmTargetLayout.For(target);
    }

    private static ILayoutInvariantValidator CreateValidator()
    {
        using var services = new ServiceCollection()
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<ILayoutInvariantValidator>();
    }
}
