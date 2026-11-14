// From the compiler harness's NumberFormatInfoFloatingFormatCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;

namespace NumberFormatInfoFloatingFixture;

public static class EntryPoint
{
    private static int _failure;

    private static void Check(bool condition, int code)
    {
        if (!condition && _failure == 0) _failure = code;
    }

    public static int Run(int input)
    {
        var nfi = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        nfi.NumberDecimalSeparator = ",";
        nfi.NumberGroupSeparator = "'";
        nfi.NegativeSign = "~";
        nfi.NaNSymbol = "nan";
        nfi.PositiveInfinitySymbol = "inf";
        nfi.NegativeInfinitySymbol = "~inf";
        nfi.PercentSymbol = "pct";
        nfi.PercentDecimalSeparator = ",";
        nfi.CurrencySymbol = "$$";
        nfi.CurrencyDecimalSeparator = ",";
        nfi.CurrencyGroupSeparator = "'";

        Check(1.5.ToString(nfi) == "1,5", 1);
        Check((-1.5).ToString(nfi) == "~1,5", 2);
        Check(double.NaN.ToString(nfi) == "nan", 3);
        Check(double.PositiveInfinity.ToString(nfi) == "inf", 4);
        Check(double.NegativeInfinity.ToString(nfi) == "~inf", 5);
        Check(1234.5.ToString("N2", nfi) == "1'234,50", 6);
        Check(1234.5.ToString("F1", nfi) == "1234,5", 7);
        Check(1.5e10.ToString("E2", nfi) == "1,50E+010", 8);
        Check(1234.5.ToString("#,##0.00", nfi) == "1'234,50", 9);
        Check(1.5f.ToString(nfi) == "1,5", 10);
        Check(1.50m.ToString(nfi) == "1,50", 11);
        Check((-2.5m).ToString("N1", nfi) == "~2,5", 12);
        Check(1234.5m.ToString("N2", nfi) == "1'234,50", 13);
        Check(0.5.ToString("P0", nfi) == "50 pct", 14);
        Check(1234.5.ToString("C2", nfi) == "$$1'234,50", 15);

        Span<char> buffer = stackalloc char[32];
        Check(2.25.TryFormat(buffer, out var written, "F2", nfi) && new string(buffer[..written]) == "2,25", 16);
        Check(2.25m.TryFormat(buffer, out written, default, nfi) && new string(buffer[..written]) == "2,25", 17);
        Check(2.5f.TryFormat(buffer, out written, default, nfi) && new string(buffer[..written]) == "2,5", 18);

        // Without a provider the text is the invariant culture's, as before.
        Check(1.5.ToString() == "1.5" && (-0.0).ToString() == "-0" && 0.1.ToString() == "0.1", 19);
        Check(1e21.ToString() == "1E+21" && double.MaxValue.ToString() == "1.7976931348623157E+308", 20);
        Check(3.14159.ToString("R") == "3.14159" && 1.0.ToString("F3") == "1.000", 21);
        Check(123.456.ToString("E3") == "1.235E+002" && 1.50m.ToString() == "1.50", 22);
        Check(0.5.ToString("P0") == "50 %" && double.NaN.ToString() == "NaN", 23);

        // .NET 11 formats X as the hexadecimal significand and binary exponent, with the provider's sign and separator.
        Check(1.5.ToString("X") == "0X1.8P+0" && 1.5.ToString("x") == "0x1.8p+0", 24);
        Check((-1.5).ToString("X", nfi) == "~0X1,8P+0" && 0.1.ToString("X4") == "0X1.999AP-4", 25);

        return _failure == 0 ? input + 1 : _failure;
    }
}
