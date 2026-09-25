using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class UnsupportedCilBoundaryTests(CorrectnessTestRunner runner) : CompilerRejectionTestBase(runner)
{
    public static TheoryData<string, byte[]> UnsupportedOpcodes => new()
    {
        { "tail.", [0xfe, 0x14, 0x2a] },
        { "jmp", [0x27, 0x2a] },
        { "arglist", [0xfe, 0x00, 0x2a] },
        { "mkrefany", [0xc6, 0x2a] },
        { "refanyval", [0xc2, 0x2a] },
        { "refanytype", [0xfe, 0x1d, 0x2a] },
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
