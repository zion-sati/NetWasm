// From the compiler harness's NumberFormatInfoParsingCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;

namespace NumberFormatInfoParsingFixture;

public static class EntryPoint
{
    private static int _failure;

    private static void Check(bool condition, int code)
    {
        if (!condition && _failure == 0) _failure = code;
    }

    private static void Throws<TException>(Action action, int code) where TException : Exception
    {
        try
        {
            action();
            Check(false, code);
        }
        catch (TException)
        {
        }
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

        Check(int.Parse("~42", nfi) == -42, 1);
        Check(int.Parse(" 1'234 ", NumberStyles.Integer | NumberStyles.AllowThousands, nfi) == 1234, 2);
        // A hyphen is accepted only when the negative sign is a minus-like character.
        Check(!int.TryParse("-42", NumberStyles.Integer, nfi, out _), 3);
        Check(short.Parse("~32768", nfi) == short.MinValue && long.Parse("9223372036854775807", nfi) == long.MaxValue, 4);
        Check(int.Parse("ff", NumberStyles.HexNumber, nfi) == 255 && sbyte.Parse("80", NumberStyles.HexNumber, nfi) == -128, 5);
        Check(ulong.Parse("FFFFFFFFFFFFFFFF", NumberStyles.HexNumber, nfi) == ulong.MaxValue, 6);
        Check(nint.Parse("~5", nfi) == -5 && nuint.Parse("7", nfi) == 7, 7);
        Check(int.Parse("~42"u8, NumberStyles.Integer, nfi) == -42 && ushort.Parse("65535"u8, nfi) == ushort.MaxValue, 8);

        Check(double.Parse("1,5", nfi) == 1.5 && double.Parse("~1'234,5", nfi) == -1234.5, 9);
        Check(double.IsNaN(double.Parse("NAN", nfi)) && double.Parse("inf", nfi) == double.PositiveInfinity, 10);
        Check(double.Parse("~inf", nfi) == double.NegativeInfinity, 11);
        // Only the provider's symbols are special values.
        Check(!double.TryParse("Infinity", NumberStyles.Float, nfi, out _), 12);
        Check(float.Parse("2,25", nfi) == 2.25f && double.Parse("1,5"u8, NumberStyles.Float, nfi) == 1.5, 13);

        Check(decimal.Parse("~2,50", nfi).ToString(nfi) == "~2,50" && decimal.Parse("1'234,5", nfi) == 1234.5m, 14);

        // Without a provider the current culture applies; it is invariant here.
        Check(int.Parse("-7") == -7 && double.Parse("1.5") == 1.5 && decimal.Parse("-0.10").ToString() == "-0.10", 15);
        Check(double.Parse("1e400") == double.PositiveInfinity && double.Parse("4.9406564584124654E-324") == double.Epsilon, 16);
        Check(BitConverter.DoubleToInt64Bits(double.Parse("0.1")) == 0x3FB999999999999A, 17);
        Check(double.Parse("1.7976931348623157E+308") == double.MaxValue && float.Parse("3.4028235E+38") == float.MaxValue, 18);

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NegativeSign = "~";
        CultureInfo.CurrentCulture = culture;
        Check(int.Parse("~3") == -3 && double.Parse("0,25") == 0.25 && decimal.Parse("~1,5") == -1.5m, 19);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        Throws<OverflowException>(() => byte.Parse("256"), 20);
        Throws<OverflowException>(() => int.Parse("2147483648"), 21);
        Throws<OverflowException>(() => decimal.Parse("79228162514264337593543950336"), 22);
        Throws<FormatException>(() => int.Parse("12a"), 23);
        Throws<FormatException>(() => double.Parse("1.5.5"), 24);
        Throws<ArgumentNullException>(() => int.Parse(null!), 25);
        Throws<ArgumentException>(() => double.Parse("1", NumberStyles.HexNumber), 26);
        Check(!int.TryParse("2147483648", out _) && !int.TryParse((string?)null, out _), 27);

        return _failure == 0 ? input + 1 : _failure;
    }
}
