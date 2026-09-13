// Adapted from dotnet/runtime System.Private.CoreLib DateOnly.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public readonly struct DateOnly : IComparable, IComparable<DateOnly>, IEquatable<DateOnly>,
        IFormattable, IParsable<DateOnly>, ISpanFormattable, ISpanParsable<DateOnly>,
        IUtf8SpanFormattable
    {
        private readonly uint _dayNumber;

        public DateOnly(int year, int month, int day) =>
            _dayNumber = (uint)GregorianDateMath.DateToDayNumber(year, month, day);

        private DateOnly(uint dayNumber) => _dayNumber = dayNumber;

        public static DateOnly MinValue { get { return new(0); } }
        public static DateOnly MaxValue { get { return new((uint)GregorianDateMath.DaysTo10000 - 1); } }
        public int DayNumber { get { return (int)_dayNumber; } }
        public int Year { get { GetParts(out var year, out _, out _); return year; } }
        public int Month { get { GetParts(out _, out var month, out _); return month; } }
        public int Day { get { GetParts(out _, out _, out var day); return day; } }
        public DayOfWeek DayOfWeek { get { return (DayOfWeek)((_dayNumber + 1) % 7); } }
        public int DayOfYear
        {
            get
            {
                return (int)(_dayNumber - (uint)GregorianDateMath.DateToDayNumber(Year, 1, 1) + 1);
            }
        }

        public DateOnly AddDays(int value)
        {
            var dayNumber = _dayNumber + (uint)value;
            if (dayNumber >= GregorianDateMath.DaysTo10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new DateOnly(dayNumber);
        }

        public DateOnly AddMonths(int value)
        {
            if (value < -120000 || value > 120000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            var year = Year;
            var month = Month;
            var totalMonths = (year - 1) * 12 + month - 1 + value;
            if (totalMonths < 0 || totalMonths >= 9999 * 12)
            {
                throw new ArgumentOutOfRangeException();
            }
            year = totalMonths / 12 + 1;
            month = totalMonths % 12 + 1;
            var day = Day;
            var daysInMonth = DaysInMonth(year, month);
            if (day > daysInMonth)
            {
                day = daysInMonth;
            }
            return new DateOnly(year, month, day);
        }

        public DateOnly AddYears(int value)
        {
            if (value < -10000 || value > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return AddMonths(value * 12);
        }

        public static DateOnly FromDayNumber(int dayNumber)
        {
            if ((uint)dayNumber >= GregorianDateMath.DaysTo10000)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new DateOnly((uint)dayNumber);
        }

        public static int DaysInMonth(int year, int month)
        {
            var first = GregorianDateMath.DateToDayNumber(year, month, 1);
            var next = month == 12 && year == 9999
                ? GregorianDateMath.DaysTo10000
                : month == 12
                ? GregorianDateMath.DateToDayNumber(year + 1, 1, 1)
                : GregorianDateMath.DateToDayNumber(year, month + 1, 1);
            return next - first;
        }

        public static bool IsLeapYear(int year) => GregorianDateMath.IsLeapYear(year);
        public static DateOnly FromDateTime(DateTime dateTime) =>
            new((uint)(dateTime.Ticks / TimeSpan.TicksPerDay));
        public DateTime ToDateTime(TimeOnly time) => new(
            checked((long)_dayNumber * TimeSpan.TicksPerDay + time.Ticks));
        public DateTime ToDateTime(TimeOnly time, DateTimeKind kind) => new(
            checked((long)_dayNumber * TimeSpan.TicksPerDay + time.Ticks), kind);
        public int CompareTo(DateOnly value) => _dayNumber.CompareTo(value._dayNumber);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is DateOnly other
                ? CompareTo(other)
                : throw new ArgumentException();
        public bool Equals(DateOnly value) => _dayNumber == value._dayNumber;
        public override bool Equals(object? value) => value is DateOnly other && Equals(other);
        public override int GetHashCode() => (int)_dayNumber;

        public void Deconstruct(out int year, out int month, out int day) =>
            GetParts(out year, out month, out day);
        public string ToLongDateString() => ToString();
        public string ToShortDateString() => ToString();
        public string ToString(IFormatProvider? provider) => ToString();
        public string ToString(string? format) => FormatInvariant(format);
        public string ToString(string? format, IFormatProvider? provider) => FormatInvariant(format);
        public override string ToString() => InvariantDateTimeText.FourDigits(Year) + "-"
            + InvariantDateTimeText.TwoDigits(Month) + "-"
            + InvariantDateTimeText.TwoDigits(Day);

        public bool TryFormat(Span<char> destination, out int charsWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
            TryCopy(FormatInvariant(format.Length == 0 ? null : string.Create(format.ToArray())),
                destination, out charsWritten);

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null)
        {
            var text = FormatInvariant(format.Length == 0 ? null : string.Create(format.ToArray()));
            if (utf8Destination.Length < text.Length)
            {
                bytesWritten = 0;
                return false;
            }
            for (var index = 0; index < text.Length; index++) utf8Destination[index] = (byte)text[index];
            bytesWritten = text.Length;
            return true;
        }

        public static DateOnly Parse(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            if (!TryParse(value, out var result))
            {
                throw new FormatException();
            }
            return result;
        }

        public static DateOnly Parse(string value, IFormatProvider? provider) => Parse(value);

        public static DateOnly Parse(string value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ValidateStyles(style);
            return TryParse(value, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static DateOnly Parse(ReadOnlySpan<char> value, IFormatProvider? provider) =>
            Parse(string.Create(value.ToArray()), provider);

        public static DateOnly Parse(ReadOnlySpan<char> value, IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            Parse(string.Create(value.ToArray()), provider, style);

        public static bool TryParse(string? value, out DateOnly result)
        {
            result = default;
            if (value == null || value.Length != 10 ||
                !InvariantDateTimeText.TryReadDigits(value, 0, 4, out var year) ||
                !InvariantDateTimeText.HasSeparator(value, 4, '-') ||
                !InvariantDateTimeText.TryReadDigits(value, 5, 2, out var month) ||
                !InvariantDateTimeText.HasSeparator(value, 7, '-') ||
                !InvariantDateTimeText.TryReadDigits(value, 8, 2, out var day))
            {
                return false;
            }
            try
            {
                result = new DateOnly(year, month, day);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool TryParse(string? value, IFormatProvider? provider, out DateOnly result) =>
            TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(string? value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            ValidateStyles(style);
            value = TrimWhiteSpace(value, style);
            return TryParse(value, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> value, out DateOnly result) =>
            TryParse(string.Create(value.ToArray()), out result);

        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider,
            out DateOnly result) => TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result) =>
            TryParse(string.Create(value.ToArray()), provider, style, out result);

        public static DateOnly ParseExact(string value, string format) =>
            ParseExact(value, format, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly ParseExact(string value, string format, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            return TryParseExact(value, format, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static DateOnly ParseExact(string value, string[] formats) =>
            ParseExact(value, formats, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly ParseExact(string value, string[] formats, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            return TryParseExact(value, formats, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static DateOnly ParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            ParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), provider, style);

        public static DateOnly ParseExact(ReadOnlySpan<char> value, string[] formats) =>
            ParseExact(value, formats, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly ParseExact(ReadOnlySpan<char> value, string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            ParseExact(string.Create(value.ToArray()), formats, provider, style);

        public static bool TryParseExact(string? value, string? format, out DateOnly result) =>
            TryParseExact(value, format, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(string? value, string? format, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            ValidateStyles(style);
            result = default;
            if (!IsSupportedFormat(format)) return false;
            return TryParse(value, provider, style, out result);
        }

        public static bool TryParseExact(string? value, string?[]? formats, out DateOnly result) =>
            TryParseExact(value, formats, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            ValidateStyles(style);
            result = default;
            if (formats == null) return false;
            for (var index = 0; index < formats.Length; index++)
            {
                if (TryParseExact(value, formats[index], provider, style, out result)) return true;
            }
            return false;
        }

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            out DateOnly result) => TryParseExact(value, format, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? provider, System.Globalization.DateTimeStyles style, out DateOnly result) =>
            TryParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), provider, style, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            out DateOnly result) => TryParseExact(value, formats, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            IFormatProvider? provider, System.Globalization.DateTimeStyles style, out DateOnly result) =>
            TryParseExact(string.Create(value.ToArray()), formats, provider, style, out result);

        public static bool operator ==(DateOnly left, DateOnly right) => left.Equals(right);
        public static bool operator !=(DateOnly left, DateOnly right) => !left.Equals(right);
        public static bool operator <(DateOnly left, DateOnly right) => left._dayNumber < right._dayNumber;
        public static bool operator <=(DateOnly left, DateOnly right) => left._dayNumber <= right._dayNumber;
        public static bool operator >(DateOnly left, DateOnly right) => left._dayNumber > right._dayNumber;
        public static bool operator >=(DateOnly left, DateOnly right) => left._dayNumber >= right._dayNumber;

        private void GetParts(out int year, out int month, out int day) =>
            GregorianDateMath.GetDateParts((int)_dayNumber, out year, out month, out day);

        private string FormatInvariant(string? format)
        {
            if (format is not null && format.Length != 0 &&
                format != "d" && format != "D" && format != "O" && format != "o")
            {
                throw new FormatException();
            }
            return ToString();
        }

        private static bool IsSupportedFormat(string? format) =>
            format is not null && (format.Length == 0 || format is "d" or "D" or "O" or "o");

        private static string? TrimWhiteSpace(string? value, System.Globalization.DateTimeStyles style)
        {
            if (value is null || (style & System.Globalization.DateTimeStyles.AllowWhiteSpaces) == 0)
            {
                return value;
            }
            var start = 0;
            var end = value.Length;
            while (start < end && IsWhiteSpace(value[start])) start++;
            while (end > start && IsWhiteSpace(value[end - 1])) end--;
            return start == 0 && end == value.Length ? value : value.Substring(start, end - start);
        }

        private static void ValidateStyles(System.Globalization.DateTimeStyles style)
        {
            if ((style & ~System.Globalization.DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException();
            }
        }

        private static bool IsWhiteSpace(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        private static bool TryCopy(string text, Span<char> destination, out int charsWritten)
        {
            if (destination.Length < text.Length)
            {
                charsWritten = 0;
                return false;
            }
            for (var index = 0; index < text.Length; index++) destination[index] = text[index];
            charsWritten = text.Length;
            return true;
        }
    }

    public enum DayOfWeek
    {
        Sunday,
        Monday,
        Tuesday,
        Wednesday,
        Thursday,
        Friday,
        Saturday,
    }
}
