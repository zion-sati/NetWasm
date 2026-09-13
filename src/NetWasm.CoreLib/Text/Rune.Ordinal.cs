// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Ordinal Rune members retained without culture/ICU dependencies.

namespace System.Text;

public readonly partial struct Rune
{
    public bool Equals(Rune other, StringComparison comparisonType) => comparisonType switch
    {
        StringComparison.Ordinal => Equals(other),
        StringComparison.OrdinalIgnoreCase => ToLowerOrdinal(this).Equals(ToLowerOrdinal(other)),
        _ => throw new ArgumentException(nameof(comparisonType)),
    };

    public static double GetNumericValue(Rune value) =>
        value._value is >= '0' and <= '9' ? value._value - '0' : -1;

    public static bool IsControl(Rune value) =>
        value._value <= 0x1F || value._value is >= 0x7F and <= 0x9F;

    public static bool IsDigit(Rune value) => value._value is >= '0' and <= '9';

    public static bool IsLetter(Rune value) =>
        value._value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    public static bool IsLetterOrDigit(Rune value) => IsLetter(value) || IsDigit(value);

    public static bool IsLower(Rune value) => value._value is >= 'a' and <= 'z';

    public static bool IsNumber(Rune value) => IsDigit(value);

    public static bool IsPunctuation(Rune value) => value._value switch
    {
        >= 0x21 and <= 0x2F or >= 0x3A and <= 0x40 or >= 0x5B and <= 0x60 or >= 0x7B and <= 0x7E => true,
        _ => false,
    };

    public static bool IsSeparator(Rune value) => value._value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

    public static bool IsSymbol(Rune value) => value._value is '$' or '+' or '<' or '=' or '>' or '^' or '`' or '|';

    public static bool IsUpper(Rune value) => value._value is >= 'A' and <= 'Z';

    public static bool IsWhiteSpace(Rune value) => IsSeparator(value);

    public static Rune ToLowerInvariant(Rune value) => ToLowerOrdinal(value);

    public static Rune ToLowerOrdinal(Rune value) =>
        IsUpper(value) ? new Rune(value._value + ('a' - 'A')) : value;

    public static Rune ToUpperInvariant(Rune value) => ToUpperOrdinal(value);

    public static Rune ToUpperOrdinal(Rune value) =>
        IsLower(value) ? new Rune(value._value - ('a' - 'A')) : value;

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
