// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EntryPointValidatorTests
{
    [Fact]
    public void AcceptsScalarReturn()
    {
        var entryPoint = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);

        CreateValidator().Validate(entryPoint);
    }

    [Fact]
    public void RejectsManagedValueTypeReturn()
    {
        var entryPoint = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Named(
                    EmitterTestSupport.Assembly,
                    "Tests",
                    "Value",
                    isValueType: true)),
        };

        var exception = Assert.Throws<CompilerException>(() =>
            CreateValidator().Validate(entryPoint));

        Assert.Equal(DiagnosticCode.InvalidEntryPoint, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingEntryPoint()
    {
        Assert.Throws<ArgumentNullException>(() => CreateValidator().Validate(null!));
    }

    private static IEntryPointValidator CreateValidator() => new[]
    {
        new EntryPointValidator(),
    }.Cast<IEntryPointValidator>().Single();
}
