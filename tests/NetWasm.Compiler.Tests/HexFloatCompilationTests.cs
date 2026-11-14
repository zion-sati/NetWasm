using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// .NET 11's hexadecimal floating point (NumberStyles.HexFloat and the X format), ported from RC3. The expected values
// are desktop .NET 11's for the same inputs.
public sealed class HexFloatCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void HexFloatParsingAndFormattingMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "HexFloatFixture", Source, "HexFloatFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        namespace HexFloatFixture;
        public static class EntryPoint
        {
            private static readonly (string Input, string Double, string Single, string Half)[] Parsed =
            [
                ("0x1p0", "0x3FF0000000000000", "0x3F800000", "0x3C00"),
                ("0x1.8p1", "0x4008000000000000", "0x40400000", "0x4200"),
                ("-0x1.fffffffffffffp+1023", "0xFFEFFFFFFFFFFFFF", "0xFF800000", "0xFC00"),
                ("0x1p-1074", "0x0000000000000001", "0x00000000", "0x0000"),
                ("0x1p-1075", "0x0000000000000000", "0x00000000", "0x0000"),
                ("0x1.0000000000000801p0", "0x3FF0000000000001", "0x3F800000", "0x3C00"),
                ("0X1P+4", "0x4030000000000000", "0x41800000", "0x4C00"),
                ("  0x1p2  ", "0x4010000000000000", "0x40800000", "0x4400"),
                ("0x.8p1", "0x3FF0000000000000", "0x3F800000", "0x3C00"),
                ("0x1", "FormatException", "FormatException", "FormatException"),
                ("0x1.", "FormatException", "FormatException", "FormatException"),
                ("1p0", "FormatException", "FormatException", "FormatException"),
                ("0xp1", "FormatException", "FormatException", "FormatException"),
                ("0x1p", "FormatException", "FormatException", "FormatException"),
                ("-0x0p0", "0x8000000000000000", "0x80000000", "0x8000"),
                ("+0x1p1", "0x4000000000000000", "0x40000000", "0x4000"),
                ("0x1.fffffep127", "0x47EFFFFFE0000000", "0x7F7FFFFF", "0x7C00"),
                ("0x1p-149", "0x36A0000000000000", "0x00000001", "0x0000"),
                ("0x1.ffcp15", "0x40EFFC0000000000", "0x477FE000", "0x7BFF"),
                ("0x1p-24", "0x3E70000000000000", "0x33800000", "0x0001"),
                ("0x1p1024", "0x7FF0000000000000", "0x7F800000", "0x7C00"),
                ("Infinity", "0x7FF0000000000000", "0x7F800000", "0x7C00"),
                ("NaN", "0xFFF8000000000000", "0xFFC00000", "0xFE00"),
                ("0x1_0p0", "FormatException", "FormatException", "FormatException"),
                ("0xABC.DEFp-3", "0x407579BDE0000000", "0x43ABCDEF", "0x5D5E"),
            ];

            private static readonly (long Bits, string Format, string Text)[] Formatted =
            [
                (4607182418800017408L, "X", "0X1P+0"),
                (4607182418800017408L, "x", "0x1p+0"),
                (4607182418800017408L, "X4", "0X1.0000P+0"),
                (4607182418800017408L, "x0", "0x1p+0"),
                (4607182418800017408L, "X20", "0X1.00000000000000000000P+0"),
                (4609434218613702656L, "X", "0X1.8P+0"),
                (4609434218613702656L, "x", "0x1.8p+0"),
                (4609434218613702656L, "X4", "0X1.8000P+0"),
                (4609434218613702656L, "x0", "0x2p+0"),
                (4609434218613702656L, "X20", "0X1.80000000000000000000P+0"),
                (-9223372036854775808L, "X", "-0X0P+0"),
                (-9223372036854775808L, "x", "-0x0p+0"),
                (-9223372036854775808L, "X4", "-0X0.0000P+0"),
                (-9223372036854775808L, "x0", "-0x0p+0"),
                (-9223372036854775808L, "X20", "-0X0.00000000000000000000P+0"),
                (9218868437227405311L, "X", "0X1.FFFFFFFFFFFFFP+1023"),
                (9218868437227405311L, "x", "0x1.fffffffffffffp+1023"),
                (9218868437227405311L, "X4", "0X1.0000P+1024"),
                (9218868437227405311L, "x0", "0x2p+1023"),
                (9218868437227405311L, "X20", "0X1.FFFFFFFFFFFFF0000000P+1023"),
                (1L, "X", "0X1P-1074"),
                (1L, "x", "0x1p-1074"),
                (1L, "X4", "0X1.0000P-1074"),
                (1L, "x0", "0x1p-1074"),
                (1L, "X20", "0X1.00000000000000000000P-1074"),
                (-2251799813685248L, "X", "NaN"),
                (-2251799813685248L, "x", "NaN"),
                (-2251799813685248L, "X4", "NaN"),
                (-2251799813685248L, "x0", "NaN"),
                (-2251799813685248L, "X20", "NaN"),
                (9218868437227405312L, "X", "Infinity"),
                (9218868437227405312L, "x", "Infinity"),
                (9218868437227405312L, "X4", "Infinity"),
                (9218868437227405312L, "x0", "Infinity"),
                (9218868437227405312L, "X20", "Infinity"),
                (4591870180066957722L, "X", "0X1.999999999999AP-4"),
                (4591870180066957722L, "x", "0x1.999999999999ap-4"),
                (4591870180066957722L, "X4", "0X1.999AP-4"),
                (4591870180066957722L, "x0", "0x2p-4"),
                (4591870180066957722L, "X20", "0X1.999999999999A0000000P-4"),
                (-4580169617128816640L, "X", "-0X1.FF8P+7"),
                (-4580169617128816640L, "x", "-0x1.ff8p+7"),
                (-4580169617128816640L, "X4", "-0X1.FF80P+7"),
                (-4580169617128816640L, "x0", "-0x2p+7"),
                (-4580169617128816640L, "X20", "-0X1.FF800000000000000000P+7"),
            ];

            private static string D(string s) { try { return "0x" + BitConverter.DoubleToInt64Bits(double.Parse(s, NumberStyles.HexFloat, CultureInfo.InvariantCulture)).ToString("X16"); } catch (Exception e) { return Kind(e); } }
            private static string F(string s) { try { return "0x" + BitConverter.SingleToInt32Bits(float.Parse(s, NumberStyles.HexFloat, CultureInfo.InvariantCulture)).ToString("X8"); } catch (Exception e) { return Kind(e); } }
            private static string H(string s) { try { return "0x" + BitConverter.HalfToInt16Bits(Half.Parse(s, NumberStyles.HexFloat, CultureInfo.InvariantCulture)).ToString("X4"); } catch (Exception e) { return Kind(e); } }

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
                for (var i = 0; i < Parsed.Length; i++)
                {
                    var row = Parsed[i];
                    if (D(row.Input) != row.Double) return 100 + i;
                    if (F(row.Input) != row.Single) return 200 + i;
                    if (H(row.Input) != row.Half) return 300 + i;
                }
                for (var i = 0; i < Formatted.Length; i++)
                {
                    var row = Formatted[i];
                    if (BitConverter.Int64BitsToDouble(row.Bits).ToString(row.Format, CultureInfo.InvariantCulture) != row.Text) return 400 + i;
                }
                if (1.5f.ToString("X", CultureInfo.InvariantCulture) != "0X1.8P+0" || ((Half)1.5).ToString("x", CultureInfo.InvariantCulture) != "0x1.8p+0" || float.Epsilon.ToString("X", CultureInfo.InvariantCulture) != "0X1P-149") return 1;
                if (!double.TryParse("0x1.8p1", NumberStyles.HexFloat, CultureInfo.InvariantCulture, out var parsed) || parsed != 3.0) return 2;
                foreach (var style in new[] { NumberStyles.AllowHexSpecifier, NumberStyles.HexFloat | NumberStyles.AllowThousands, NumberStyles.HexNumber, NumberStyles.AllowBinarySpecifier | NumberStyles.AllowExponent })
                {
                    try { double.Parse("0x1p0", style, CultureInfo.InvariantCulture); return 3; }
                    catch (ArgumentException e) when (e.ParamName == "style") { }
                }
                return input + 1;
            }
        }
        """;
}
