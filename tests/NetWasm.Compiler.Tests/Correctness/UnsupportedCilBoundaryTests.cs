// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class UnsupportedCilBoundaryTests(CorrectnessTestRunner runner) : CompilerRejectionTestBase(runner)
{
    public static TheoryData<string, byte[]> UnsupportedOpcodes => new()
    {
        { "jmp", [0x27, 0x2a] },
        { "arglist", [0xfe, 0x00, 0x2a] },
        // mkrefany, refanyval and refanytype lower to TypedReference's helpers (TypedReferenceCompilationTests);
        // a tail.-prefixed call runs as an ordinary call (TailCallPrefixCompilationTests).
    };

    [Theory]
    [MemberData(nameof(UnsupportedOpcodes))]
    public void LegacyAndOptionalOpcodesHaveExactUnsupportedDiagnostics(string opcode, byte[] cil)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UnsupportedOpcodeFixture",
            """
                namespace UnsupportedOpcodeFixture;

                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        var value = input;
                        value = unchecked(value * 3 + 1);
                        value = unchecked(value * 5 + 2);
                        return value;
                    }
                }
                """);
        var positive = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "UnsupportedOpcodeFixture.EntryPoint", "Run", []));
        Assert.NotEmpty(positive.ApplicationModule);

        new MethodBodyPatcher().Patch(
            assembly,
            "UnsupportedOpcodeFixture.EntryPoint",
            "Run",
            cil,
            maxStack: 2);

        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly)));
        Run(new(new("unsupported-opcode", "excluded opcode", assembly, hash, 0, "", Convert.ToHexStringLower(cil)),
            assets.CoreLib, "UnsupportedOpcodeFixture.EntryPoint", assets.Directory, 0,
            new(new(DiagnosticCode.UnsupportedCil, $"unsupported CIL opcode '{opcode}'",
                "UnsupportedOpcodeFixture.EntryPoint::Run", 0))));
    }
}
