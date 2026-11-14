using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// What .NET 11 GA changed in number conversion and parsing since RC3. Decimal converts to double, float, Half and
// BFloat16 correctly rounded, with ties to even at the subnormal boundary (#135730). Signs match the format's own
// sign strings, preferring the longer of two that overlap, in hexadecimal floating point (including the exponent,
// which formatting now writes with the format's signs; #135282) and in integer and decimal parsing. The expected
// values are desktop .NET 11.0.0's for the same inputs.
public sealed class Net11GaNumericCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NumberConversionAndSignsMatchDotNet11(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "Net11GaNumericFixture", Source, "Net11GaNumericFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        using System.Numerics;
        namespace Net11GaNumericFixture;
        public static class EntryPoint
        {
            private static readonly (decimal Value, string Double, string Single, string Half, string BFloat16)[] Converted =
            [
                (0m, "0x0000000000000000", "0x00000000", "0x0000", "0x0000"),
                (1m, "0x3FF0000000000000", "0x3F800000", "0x3C00", "0x3F80"),
                (-1m, "0xBFF0000000000000", "0xBF800000", "0xBC00", "0xBF80"),
                (0.1m, "0x3FB999999999999A", "0x3DCCCCCD", "0x2E66", "0x3DCD"),
                (12.25m, "0x4028800000000000", "0x41440000", "0x4A20", "0x4144"),
                (10000000000000.099609375m, "0x42A2309CE5400033", "0x551184E7", "0x7C00", "0x5512"),
                (79228162514264337593543950335m, "0x45F0000000000000", "0x6F800000", "0x7C00", "0x6F80"),
                (-79228162514264337593543950335m, "0xC5F0000000000000", "0xEF800000", "0xFC00", "0xEF80"),
                (0.0000000000000000000000000001m, "0x3A1FB0F6BE506019", "0x10FD87B6", "0x0000", "0x10FE"),
                (65504m, "0x40EFFC0000000000", "0x477FE000", "0x7BFF", "0x4780"),
                (65519.99m, "0x40EFFDFFAE147AE1", "0x477FEFFD", "0x7BFF", "0x4780"),
                (65520m, "0x40EFFE0000000000", "0x477FF000", "0x7C00", "0x4780"),
                (0.00006103515625m, "0x3F10000000000000", "0x38800000", "0x0400", "0x3880"),
                (0.000060975551605224609375m, "0x3F0FF80000000000", "0x387FC000", "0x03FF", "0x3880"),
                (0.000000059604644775390625m, "0x3E70000000000000", "0x33800000", "0x0001", "0x3380"),
                (0.0000000298023223876953125m, "0x3E60000000000000", "0x33000000", "0x0000", "0x3300"),
                (0.0000000298023223876953126m, "0x3E60000000000000", "0x33000000", "0x0001", "0x3300"),
                (0.0000000894069671630859375m, "0x3E78000000000000", "0x33C00000", "0x0002", "0x33C0"),
                (1.00048828125m, "0x3FF0020000000000", "0x3F801000", "0x3C00", "0x3F80"),
                (1.000732421875m, "0x3FF0030000000000", "0x3F801800", "0x3C01", "0x3F80"),
                (0.3333333333333333333333333333m, "0x3FD5555555555555", "0x3EAAAAAB", "0x3555", "0x3EAB"),
                (0.00000000000000000000000001m, "0x3A88C240C4AECB14", "0x14461206", "0x0000", "0x1446"),
                (2.7182818284590452353602874714m, "0x4005BF0A8B145769", "0x402DF854", "0x4170", "0x402E"),
                (16777217m, "0x4170000010000000", "0x4B800000", "0x7C00", "0x4B80"),
                (9007199254740993m, "0x4340000000000000", "0x5A000000", "0x7C00", "0x5A00"),
            ];

            private static readonly NumberFormatInfo Tilde = new() { NegativeSign = "~", PositiveSign = "#" };
            private static readonly NumberFormatInfo Minus = new() { NegativeSign = "−" };
            private static readonly NumberFormatInfo Overlap = new() { PositiveSign = "p", NegativeSign = "pn" };
            private static readonly NumberFormatInfo Overlap2 = new() { PositiveSign = "pn", NegativeSign = "p" };

            private static readonly (string Text, NumberFormatInfo Info, string Double)[] HexParsed =
            [
                ("~0x1p~1", Tilde, "0xBFE0000000000000"),
                ("#0x1p#1", Tilde, "0x4000000000000000"),
                ("-0x1p-1", Tilde, "FormatException"),
                ("−0x1.8p−2", Minus, "0xBFD8000000000000"),
                ("-0x1.8p-2", Minus, "0xBFD8000000000000"),
                ("pn0x1p0", Overlap, "0xBFF0000000000000"),
                ("p0x1pp1", Overlap, "0x4000000000000000"),
                ("0x1ppn1", Overlap, "0x3FE0000000000000"),
                ("pn0x1p0", Overlap2, "0x3FF0000000000000"),
                ("p0x1pp1", Overlap2, "0xBFE0000000000000"),
            ];

            private static readonly (double Value, NumberFormatInfo Info, string Text)[] HexFormatted =
            [
                (0.375, Tilde, "0X1.8P~2"),
                (-0.375, Tilde, "~0X1.8P~2"),
                (-0.375, Minus, "−0X1.8P−2"),
                (8.0, Tilde, "0X1P#3"),
                (0.0, Tilde, "0X0P#0"),
            ];

            private static readonly (string Text, NumberFormatInfo Info, string Value)[] Signed =
            [
                ("pn5", Overlap, "-5"),
                ("p5", Overlap, "5"),
                ("pn5", Overlap2, "5"),
                ("p5", Overlap2, "-5"),
                ("5pn", Overlap, "-5"),
                ("~7", Tilde, "-7"),
                ("-7", Tilde, "FormatException"),
            ];

            private static string Bits(double value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16");

            private static string Hex(string text, NumberFormatInfo info)
            {
                try { return Bits(double.Parse(text, NumberStyles.HexFloat, info)); }
                catch (Exception e) { return Kind(e); }
            }

            private static string Int(string text, NumberFormatInfo info)
            {
                try { return int.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign, info).ToString(CultureInfo.InvariantCulture); }
                catch (Exception e) { return Kind(e); }
            }

            private static string Dec(string text, NumberFormatInfo info)
            {
                try { return decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign, info).ToString(CultureInfo.InvariantCulture); }
                catch (Exception e) { return Kind(e); }
            }

            // Names the exception by type test, as a program carries no type names (issue 225).
            private static string Kind(Exception e) => e switch
            {
                FormatException => nameof(FormatException),
                OverflowException => nameof(OverflowException),
                ArgumentNullException => nameof(ArgumentNullException),
                ArgumentException => nameof(ArgumentException),
                _ => "an exception the fixture does not name",
            };

            public static int Run(int input)
            {
                for (var i = 0; i < Converted.Length; i++)
                {
                    var row = Converted[i];
                    if (Bits((double)row.Value) != row.Double || Bits(decimal.ToDouble(row.Value)) != row.Double) return 100 + i;
                    if ("0x" + BitConverter.SingleToInt32Bits((float)row.Value).ToString("X8") != row.Single) return 200 + i;
                    if ("0x" + BitConverter.HalfToUInt16Bits((Half)row.Value).ToString("X4") != row.Half) return 300 + i;
                    if ("0x" + BitConverter.BFloat16ToUInt16Bits((BFloat16)row.Value).ToString("X4") != row.BFloat16) return 400 + i;
                }
                for (var i = 0; i < HexParsed.Length; i++)
                {
                    if (Hex(HexParsed[i].Text, HexParsed[i].Info) != HexParsed[i].Double) return 500 + i;
                }
                for (var i = 0; i < HexFormatted.Length; i++)
                {
                    var row = HexFormatted[i];
                    var text = row.Value.ToString("X", row.Info);
                    if (text != row.Text) return 600 + i;
                    if (Hex(text, row.Info) != Bits(row.Value)) return 650 + i;
                }
                for (var i = 0; i < Signed.Length; i++)
                {
                    var row = Signed[i];
                    if (Int(row.Text, row.Info) != row.Value) return 700 + i;
                    if (Dec(row.Text, row.Info) != row.Value) return 750 + i;
                }
                return input + 1;
            }
        }
        """;
}
