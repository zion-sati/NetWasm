// Adapted from dotnet/runtime System.Private.CoreLib TimeOnly.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public readonly struct TimeOnly : IComparable, IComparable<TimeOnly>, IEquatable<TimeOnly>,
        IFormattable, IParsable<TimeOnly>, ISpanFormattable, ISpanParsable<TimeOnly>,
        IUtf8SpanFormattable
    {
        private readonly long _ticks;

        public TimeOnly(int hour, int minute) : this(hour, minute, 0, 0)
        {
        }
        public TimeOnly(int hour, int minute, int second) : this(hour, minute, second, 0)
        {
        }
        public TimeOnly(int hour, int minute, int second, int millisecond)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60 ||
                (uint)millisecond >= 1000)
            {
                throw new ArgumentOutOfRangeException();
            }
            _ticks = hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond
                + millisecond * TimeSpan.TicksPerMillisecond;
        }

        public TimeOnly(int hour, int minute, int second, int millisecond, int microsecond)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60 ||
                (uint)millisecond >= 1000 || (uint)microsecond >= 1000)
            {
                throw new ArgumentOutOfRangeException();
            }
            _ticks = hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond
                + millisecond * TimeSpan.TicksPerMillisecond
                + microsecond * TimeSpan.TicksPerMicrosecond;
        }

        public TimeOnly(long ticks)
        {
            if ((ulong)ticks >= (ulong)TimeSpan.TicksPerDay)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }
            _ticks = ticks;
        }
        public static TimeOnly MinValue { get { return new(0); } }
        public static TimeOnly MaxValue { get { return new(TimeSpan.TicksPerDay - 1); } }
        public long Ticks { get { return _ticks; } }
        public int Hour { get { return (int)(_ticks / TimeSpan.TicksPerHour); } }
        public int Minute { get { return (int)(_ticks / TimeSpan.TicksPerMinute % 60); } }
        public int Second { get { return (int)(_ticks / TimeSpan.TicksPerSecond % 60); } }
        public int Millisecond { get { return (int)(_ticks / TimeSpan.TicksPerMillisecond % 1000); } }
        public int Microsecond { get { return (int)(_ticks / TimeSpan.TicksPerMicrosecond % 1000); } }
        public int Nanosecond { get { return (int)(_ticks % TimeSpan.TicksPerMicrosecond) * 100; } }

        public TimeOnly Add(TimeSpan value) => AddTicks(value.Ticks);
        public TimeOnly Add(TimeSpan value, out int wrappedDays) =>
            AddTicks(value.Ticks, out wrappedDays);
        public TimeOnly AddHours(double value) => AddTicks(
            (long)(value * TimeSpan.TicksPerHour));
        public TimeOnly AddHours(double value, out int wrappedDays) => AddTicks(
            (long)(value * TimeSpan.TicksPerHour), out wrappedDays);
        public TimeOnly AddMinutes(double value) => AddTicks(
            (long)(value * TimeSpan.TicksPerMinute));
        public TimeOnly AddMinutes(double value, out int wrappedDays) => AddTicks(
            (long)(value * TimeSpan.TicksPerMinute), out wrappedDays);

        public bool IsBetween(TimeOnly start, TimeOnly end)
        {
            return start._ticks <= end._ticks
                ? _ticks - start._ticks < end._ticks - start._ticks
                : _ticks - end._ticks >= start._ticks - end._ticks;
        }

        public static TimeOnly FromTicks(long ticks)
        {
            if ((ulong)ticks >= (ulong)TimeSpan.TicksPerDay)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new TimeOnly(ticks);
        }
        public static TimeOnly FromTimeSpan(TimeSpan timeSpan) => new(timeSpan.Ticks);
        public static TimeOnly FromDateTime(DateTime dateTime) =>
            FromTicks(dateTime.TimeOfDay.Ticks);
        public TimeSpan ToTimeSpan() => new(_ticks);

        public int CompareTo(TimeOnly value) => _ticks.CompareTo(value._ticks);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is TimeOnly other
                ? CompareTo(other)
                : throw new ArgumentException();
        public bool Equals(TimeOnly value) => _ticks == value._ticks;
        public override bool Equals(object? value) => value is TimeOnly other && Equals(other);
        public override int GetHashCode() => unchecked((int)_ticks) ^ (int)(_ticks >> 32);

        public void Deconstruct(out int hour, out int minute)
        {
            hour = Hour;
            minute = Minute;
        }

        public void Deconstruct(out int hour, out int minute, out int second)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
        }

        public void Deconstruct(out int hour, out int minute, out int second, out int millisecond)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
            millisecond = Millisecond;
        }

        public void Deconstruct(
            out int hour, out int minute, out int second, out int millisecond, out int microsecond)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
            millisecond = Millisecond;
            microsecond = Microsecond;
        }
        public string ToLongTimeString() => ToString();
        public string ToShortTimeString() => ToString();
        public string ToString(IFormatProvider? provider) => ToString();
        public string ToString(string? format) => FormatInvariant(format);
        public string ToString(string? format, IFormatProvider? provider) => FormatInvariant(format);
        public override string ToString() => InvariantDateTimeText.TwoDigits(Hour) + ":"
            + InvariantDateTimeText.TwoDigits(Minute) + ":"
            + InvariantDateTimeText.TwoDigits(Second)
            + InvariantDateTimeText.Fraction(_ticks);

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

        public static TimeOnly Parse(string value)
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

        public static TimeOnly Parse(string value, IFormatProvider? provider) => Parse(value);

        public static TimeOnly Parse(string value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ValidateStyles(style);
            return TryParse(value, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static TimeOnly Parse(ReadOnlySpan<char> value, IFormatProvider? provider) =>
            Parse(string.Create(value.ToArray()), provider);

        public static TimeOnly Parse(ReadOnlySpan<char> value, IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            Parse(string.Create(value.ToArray()), provider, style);
        public static bool TryParse(string? value, out TimeOnly result)
        {
            result = default;
            if (value == null || value.Length < 5 ||
                !InvariantDateTimeText.TryReadDigits(value, 0, 2, out var hour) ||
                !InvariantDateTimeText.HasSeparator(value, 2, ':') ||
                !InvariantDateTimeText.TryReadDigits(value, 3, 2, out var minute))
            {
                return false;
            }
            var second = 0;
            var fraction = 0L;
            if (value.Length > 5)
            {
                if (!InvariantDateTimeText.HasSeparator(value, 5, ':') ||
                    !InvariantDateTimeText.TryReadDigits(value, 6, 2, out second))
                {
                    return false;
                }
                if (value.Length > 8 &&
                    (!InvariantDateTimeText.HasSeparator(value, 8, '.') ||
                     !InvariantDateTimeText.TryReadFraction(value, 9, value.Length, out fraction)))
                {
                    return false;
                }
            }
            if (hour >= 24 || minute >= 60 || second >= 60)
            {
                return false;
            }
            result = new TimeOnly(hour * TimeSpan.TicksPerHour
                + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond
                + fraction);
            return true;
        }

        public static bool TryParse(string? value, IFormatProvider? provider, out TimeOnly result) =>
            TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(string? value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
        {
            ValidateStyles(style);
            value = TrimWhiteSpace(value, style);
            return TryParse(value, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> value, out TimeOnly result) =>
            TryParse(string.Create(value.ToArray()), out result);

        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider,
            out TimeOnly result) => TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result) =>
            TryParse(string.Create(value.ToArray()), provider, style, out result);

        public static TimeOnly ParseExact(string value, string format) =>
            ParseExact(value, format, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly ParseExact(string value, string format, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            return TryParseExact(value, format, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static TimeOnly ParseExact(string value, string[] formats) =>
            ParseExact(value, formats, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly ParseExact(string value, string[] formats, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            return TryParseExact(value, formats, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static TimeOnly ParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            ParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), provider, style);

        public static TimeOnly ParseExact(ReadOnlySpan<char> value, string[] formats) =>
            ParseExact(value, formats, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly ParseExact(ReadOnlySpan<char> value, string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            ParseExact(string.Create(value.ToArray()), formats, provider, style);

        public static bool TryParseExact(string? value, string? format, out TimeOnly result) =>
            TryParseExact(value, format, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(string? value, string? format, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
        {
            ValidateStyles(style);
            result = default;
            if (!IsSupportedFormat(format)) return false;
            return TryParse(value, provider, style, out result);
        }

        public static bool TryParseExact(string? value, string?[]? formats, out TimeOnly result) =>
            TryParseExact(value, formats, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
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
            out TimeOnly result) => TryParseExact(value, format, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? provider, System.Globalization.DateTimeStyles style, out TimeOnly result) =>
            TryParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), provider, style, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            out TimeOnly result) => TryParseExact(value, formats, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            IFormatProvider? provider, System.Globalization.DateTimeStyles style, out TimeOnly result) =>
            TryParseExact(string.Create(value.ToArray()), formats, provider, style, out result);
        public static TimeSpan operator -(TimeOnly left, TimeOnly right)
        {
            var difference = left._ticks - right._ticks;
            return new TimeSpan(difference + ((difference >> 63) & TimeSpan.TicksPerDay));
        }
        public static bool operator ==(TimeOnly left, TimeOnly right) => left.Equals(right);
        public static bool operator !=(TimeOnly left, TimeOnly right) => !left.Equals(right);
        public static bool operator <(TimeOnly left, TimeOnly right) => left._ticks < right._ticks;
        public static bool operator <=(TimeOnly left, TimeOnly right) => left._ticks <= right._ticks;
        public static bool operator >(TimeOnly left, TimeOnly right) => left._ticks > right._ticks;
        public static bool operator >=(TimeOnly left, TimeOnly right) => left._ticks >= right._ticks;

        private TimeOnly AddTicks(long ticks)
        {
            return new TimeOnly(Wrap(_ticks + ticks % TimeSpan.TicksPerDay));
        }

        private TimeOnly AddTicks(long ticks, out int wrappedDays)
        {
            var days = ticks / TimeSpan.TicksPerDay;
            var newTicks = ticks % TimeSpan.TicksPerDay + _ticks;
            if (newTicks < 0)
            {
                days--;
                newTicks += TimeSpan.TicksPerDay;
            }
            else if (newTicks >= TimeSpan.TicksPerDay)
            {
                days++;
                newTicks -= TimeSpan.TicksPerDay;
            }
            wrappedDays = checked((int)days);
            return new TimeOnly(newTicks);
        }

        private static long Wrap(long ticks)
        {
            ticks %= TimeSpan.TicksPerDay;
            return ticks < 0 ? ticks + TimeSpan.TicksPerDay : ticks;
        }

        private string FormatInvariant(string? format)
        {
            if (format is not null && format.Length != 0 &&
                format != "t" && format != "T" && format != "g" && format != "G" &&
                format != "O" && format != "o")
            {
                throw new FormatException();
            }
            return ToString();
        }

        private static bool IsSupportedFormat(string? format) =>
            format is not null && (format.Length == 0 || format is "t" or "T" or "g" or "G" or "O" or "o");

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
}
