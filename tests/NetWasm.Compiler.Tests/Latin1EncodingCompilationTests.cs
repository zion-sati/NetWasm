using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Encoding.Latin1 (ISO-8859-1, code page 28591): each byte is the char of the same value, and a char above U+00FF
// goes to the best-fit fallback (curly quotes to straight ones, others to '?'), or to the fallback a caller chooses.
// The expected values are desktop .NET 11.0.0's.
public sealed class Latin1EncodingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void Latin1MatchesDotNet11(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "Latin1Fixture", Source, "Latin1Fixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Text;
        namespace Latin1Fixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var latin1 = Encoding.Latin1;
                if (latin1.CodePage != 28591 || latin1.WebName != "iso-8859-1" || latin1.EncodingName != "Western European (ISO)" || !latin1.IsSingleByte) return 1;
                if (BitConverter.ToString(latin1.GetBytes("Aéÿ‘’“€Ā😀")) != "41-E9-FF-27-27-22-3F-41-3F-3F") return 2;
                var text = latin1.GetString(new byte[] { 0x41, 0xE9, 0xFF, 0x80 });
                if (text.Length != 4 || text[3] != '\u0080' || text[1] != 'é') return 3;
                if (Encoding.GetEncoding("ISO-8859-1").CodePage != 28591 || Encoding.GetEncoding(28591).WebName != "iso-8859-1" || Encoding.GetEncoding("latin1").CodePage != 28591) return 4;
                if (latin1.GetMaxByteCount(10) != 11 || latin1.GetMaxCharCount(10) != 10) return 5;
                var strict = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                try { strict.GetBytes("€"); return 6; } catch (EncoderFallbackException) { }
                return input + 1;
            }
        }
        """;
}
