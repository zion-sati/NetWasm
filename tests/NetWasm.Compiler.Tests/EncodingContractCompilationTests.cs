using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class EncodingContractCompilationTests
{
    [Fact]
    public void CompilerExecutesEncodingDefaultsCloneAndFallbackContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "EncodingContractFixture",
            """
            using System.Text;

            namespace EncodingContractFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (input == 1)
                    {
                        return new UTF8Encoding(true).GetPreamble().Length == 3 &&
                            new UTF8Encoding(false).GetPreamble().Length == 0 ? 1 : 0;
                    }

                if (input == 2)
                {
                    var clone = (Encoding)Encoding.UTF8.Clone();
                    return clone.IsReadOnly ? 0 : 1;
                }

                    var encoding = (Encoding)new ASCIIEncoding().Clone();
                    encoding.EncoderFallback = new EncoderReplacementFallback("X");
                    return encoding.GetBytes("AΩ")[1] == (byte)'X' ? input : 0;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EncodingContractFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 1));
        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 2));
        Assert.Equal(3, ExecuteWithNode(result.ApplicationModule, assets.Directory, 3));
    }
}
