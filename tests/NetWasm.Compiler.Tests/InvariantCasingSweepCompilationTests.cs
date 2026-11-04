using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// NetWasm runs .NET's invariant globalization mode, so its casing is compared with desktop .NET 11
// RC3 (11.0.0-rc.3.26522.148) run with InvariantGlobalization, where the differential corpus's
// desktop side runs ICU: ordinal casing over every BMP character, whose lower casing keeps the
// Kelvin, Ohm and Angstrom signs in their ordinal upper-casing class, and TextInfo's Rune casing
// over planes 0 and 1, which hold every scalar value that changes case.
public sealed class InvariantCasingSweepCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, 0, 1331783408)]
    [InlineData(WasmTarget.Wasm32, false, 1, -1290944027)]
    [InlineData(WasmTarget.Wasm32, true, 0, 1331783408)]
    [InlineData(WasmTarget.Wasm32, true, 1, -1290944027)]
    [InlineData(WasmTarget.Wasm64, true, 0, 1331783408)]
    [InlineData(WasmTarget.Wasm64, true, 1, -1290944027)]
    public void CasingMatchesInvariantDesktop(WasmTarget target, bool optimize, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"InvariantCasingSweep_{target}_{optimize}_{input}",
            Source,
            "EntryPoint",
            optimize,
            target,
            input,
            [])));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        using System.Text;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                if (input == 0)
                {
                    for (var c = 0; c <= char.MaxValue; c++)
                    {
                        hash = unchecked(hash * 31 + char.ToUpperOrdinal((char)c));
                        hash = unchecked(hash * 31 + char.ToLowerOrdinal((char)c));
                    }
                    return hash;
                }
                var textInfo = CultureInfo.InvariantCulture.TextInfo;
                for (var value = 0; value <= 0x1FFFF; value++)
                {
                    if (!Rune.IsValid(value)) continue;
                    var rune = new Rune(value);
                    hash = unchecked(hash * 31 + textInfo.ToUpper(rune).Value);
                    hash = unchecked(hash * 31 + textInfo.ToLower(rune).Value);
                }
                return hash;
            }
        }
        """;
}
