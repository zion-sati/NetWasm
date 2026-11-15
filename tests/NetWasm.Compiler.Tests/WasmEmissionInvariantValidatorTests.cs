// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class WasmEmissionInvariantValidatorTests
{
    [Fact]
    public void AcceptsACompilerModuleAndRejectsInvalidSectionStructure()
    {
        var validator = CreateValidator();

        validator.Validate(
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0],
            WasmTarget.Wasm32);

        byte[] outOfOrder =
        [
            0, 0x61, 0x73, 0x6d, 1, 0, 0, 0,
            10, 0,
            1, 0,
        ];
        var exception = Assert.Throws<CompilerException>(() =>
            validator.Validate(outOfOrder, WasmTarget.Wasm32));
        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains("out of order", exception.Message);
    }

    [Fact]
    public void ValidatesEverySectionAndEveryMalformedEncodingContract()
    {
        var validator = CreateValidator();
        byte[] everySection =
        [
            0, 0x61, 0x73, 0x6d, 1, 0, 0, 0,
            1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 13, 0, 6, 0,
            7, 0, 8, 0, 9, 0, 12, 0, 10, 0, 11, 0,
        ];

        validator.Validate(everySection, WasmTarget.Wasm64);

        byte[][] malformed =
        [
            [],
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 1],
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 1],
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 1, 1],
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 1, 0, 1, 0],
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 14, 0],
            [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 1, 0x80, 0x80, 0x80, 0x80, 0x80],
        ];

        Assert.All(malformed, bytes => Assert.Throws<CompilerException>(() =>
            validator.Validate(bytes, WasmTarget.Wasm32)));
    }

    [Fact]
    public void RejectsSectionSizesThatExceedInt32()
    {
        var oversized = new byte[]
        {
            0, 0x61, 0x73, 0x6d, 1, 0, 0, 0,
            1,
            0xff, 0xff, 0xff, 0xff, 0x0f,
        };

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            oversized,
            WasmTarget.Wasm64));

        Assert.Contains("exceeds the serialized module", exception.Message);
    }

    private static IWasmEmissionInvariantValidator CreateValidator()
    {
        using var services = new ServiceCollection()
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<IWasmEmissionInvariantValidator>();
    }
}
