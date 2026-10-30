// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.
// Ordinal Rune members retained without culture/ICU dependencies.

namespace System.Text;

using System.Globalization;

public readonly partial struct Rune
{
    public bool Equals(Rune other, StringComparison comparisonType) => comparisonType switch
    {
        StringComparison.Ordinal => Equals(other),
        StringComparison.OrdinalIgnoreCase => ToLowerOrdinal(this).Equals(ToLowerOrdinal(other)),
        _ => throw new ArgumentException(nameof(comparisonType)),
    };

    public static double GetNumericValue(Rune value) => CharUnicodeInfo.GetNumericValue(value._value);

    public static UnicodeCategory GetUnicodeCategory(Rune value) =>
        CharUnicodeInfo.GetUnicodeCategory(value._value);

    public static bool IsControl(Rune value) =>
        value._value <= 0x1F || value._value is >= 0x7F and <= 0x9F;

    public static bool IsDigit(Rune value) => GetUnicodeCategory(value) == UnicodeCategory.DecimalDigitNumber;

    public static bool IsLetter(Rune value) => IsCategoryInRange(
        GetUnicodeCategory(value), UnicodeCategory.UppercaseLetter, UnicodeCategory.OtherLetter);

    public static bool IsLetterOrDigit(Rune value) => IsLetter(value) || IsDigit(value);

    public static bool IsLower(Rune value) => GetUnicodeCategory(value) == UnicodeCategory.LowercaseLetter;

    public static bool IsNumber(Rune value) => IsCategoryInRange(
        GetUnicodeCategory(value), UnicodeCategory.DecimalDigitNumber, UnicodeCategory.OtherNumber);

    public static bool IsPunctuation(Rune value) => IsCategoryInRange(
        GetUnicodeCategory(value), UnicodeCategory.ConnectorPunctuation, UnicodeCategory.OtherPunctuation);

    public static bool IsSeparator(Rune value) => IsCategoryInRange(
        GetUnicodeCategory(value), UnicodeCategory.SpaceSeparator, UnicodeCategory.ParagraphSeparator);

    public static bool IsSymbol(Rune value) => IsCategoryInRange(
        GetUnicodeCategory(value), UnicodeCategory.MathSymbol, UnicodeCategory.OtherSymbol);

    public static bool IsUpper(Rune value) => GetUnicodeCategory(value) == UnicodeCategory.UppercaseLetter;

    public static bool IsWhiteSpace(Rune value) => value.IsBmp && CharUnicodeInfo.GetIsWhiteSpace((char)value._value);

    public static Rune ToLowerInvariant(Rune value) => ToLowerOrdinal(value);

    public static Rune ToLowerOrdinal(Rune value) => new(CharUnicodeInfo.ToLower((uint)value._value));

    public static Rune ToUpperInvariant(Rune value) => ToUpperOrdinal(value);

    public static Rune ToUpperOrdinal(Rune value) => new(CharUnicodeInfo.ToUpper((uint)value._value));

    private static bool IsCategoryInRange(UnicodeCategory value, UnicodeCategory first, UnicodeCategory last) =>
        value >= first && value <= last;

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider) => TryEncodeToUtf16(destination, out charsWritten);

    bool ISpanFormattable.TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider) => TryEncodeToUtf16(destination, out charsWritten);

    bool IUtf8SpanFormattable.TryFormat(
        Span<byte> destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider) => TryEncodeToUtf8(destination, out bytesWritten);

    static Rune IParsable<Rune>.Parse(string value, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(value);
        var status = DecodeFromUtf16(new ReadOnlySpan<char>(value.ToCharArray()), out var result, out var consumed);
        if (status != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            throw new FormatException();
        }

        return result;
    }

    static bool IParsable<Rune>.TryParse(string? value, IFormatProvider? provider, out Rune result)
    {
        if (value is null)
        {
            result = ReplacementChar;
            return false;
        }

        var status = DecodeFromUtf16(new ReadOnlySpan<char>(value.ToCharArray()), out result, out var consumed);
        if (status != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            result = ReplacementChar;
            return false;
        }

        return true;
    }

    static Rune ISpanParsable<Rune>.Parse(ReadOnlySpan<char> value, IFormatProvider? provider)
    {
        var status = DecodeFromUtf16(value, out var result, out var consumed);
        if (status != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            throw new FormatException();
        }

        return result;
    }

    static bool ISpanParsable<Rune>.TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out Rune result)
    {
        var status = DecodeFromUtf16(value, out result, out var consumed);
        if (status != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            result = ReplacementChar;
            return false;
        }

        return true;
    }

    static Rune IUtf8SpanParsable<Rune>.Parse(ReadOnlySpan<byte> value, IFormatProvider? provider)
    {
        var status = DecodeFromUtf8(value, out var result, out var consumed);
        if (status != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            throw new FormatException();
        }

        return result;
    }

    static bool IUtf8SpanParsable<Rune>.TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out Rune result)
    {
        var status = DecodeFromUtf8(value, out result, out var consumed);
        if (status != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            result = ReplacementChar;
            return false;
        }

        return true;
    }
}
