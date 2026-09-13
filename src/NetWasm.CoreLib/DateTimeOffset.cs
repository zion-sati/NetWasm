// Adapted from dotnet/runtime System.Private.CoreLib DateTimeOffset.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using Runtime.InteropServices;
    using Runtime.InteropServices.TimeZones;

    public readonly struct DateTimeOffset : IComparable, IComparable<DateTimeOffset>, IEquatable<DateTimeOffset>,
        IFormattable, IParsable<DateTimeOffset>, ISpanFormattable, ISpanParsable<DateTimeOffset>,
        IUtf8SpanFormattable
    {
        private const int MaxOffsetMinutes = 14 * 60;
        private const long FileTimeOffsetTicks = 504911232000000000L;
        private const long UnixEpochTicks = GregorianDateMath.UnixEpochTicks;
        private readonly long _utcTicks;
        private readonly short _offsetMinutes;

        public static readonly DateTimeOffset MinValue = new(DateTime.MinValue.Ticks, TimeSpan.Zero);
        public static readonly DateTimeOffset MaxValue = new(DateTime.MaxTicks, TimeSpan.Zero);
        public static readonly DateTimeOffset UnixEpoch = new(UnixEpochTicks, TimeSpan.Zero);

        public DateTimeOffset(long ticks, TimeSpan offset)
        {
            ValidateOffset(offset);
            if ((ulong)ticks > (ulong)DateTime.MaxTicks)
            {
                throw new ArgumentOutOfRangeException();
            }
            var utcTicks = checked(ticks - offset.Ticks);
            if ((ulong)utcTicks > (ulong)DateTime.MaxTicks)
            {
                throw new ArgumentOutOfRangeException();
            }
            _utcTicks = utcTicks;
            _offsetMinutes = (short)(offset.Ticks / TimeSpan.TicksPerMinute);
        }

        public DateTimeOffset(DateTime dateTime, TimeSpan offset) : this(dateTime.Ticks, offset)
        {
            if (dateTime.Kind == DateTimeKind.Utc && offset != TimeSpan.Zero)
            {
                throw new ArgumentException();
            }
            if (dateTime.Kind == DateTimeKind.Local)
            {
                PlatformServices.EnsureLocalTimeReady();
                var localOffset = PlatformServices.LocalTimeOffsets.Resolve(
                    dateTime.Ticks,
                    LocalTimeBasis.Local);
                if (offset.Ticks != localOffset.Ticks)
                {
                    throw new ArgumentException();
                }
            }
        }

        public DateTimeOffset(DateTime dateTime) : this(
            dateTime.Ticks,
            dateTime.Kind == DateTimeKind.Utc
                ? TimeSpan.Zero
                : ResolveLocalOffset(dateTime.Ticks))
        {
        }

        public DateTimeOffset(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            TimeSpan offset) : this(new DateTime(year, month, day, hour, minute, second), offset)
        {
        }

        public DateTimeOffset(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            int millisecond,
            TimeSpan offset) : this(
                checked(new DateTime(year, month, day, hour, minute, second).Ticks
                    + (long)millisecond * TimeSpan.TicksPerMillisecond), offset)
        {
            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        public DateTimeOffset(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            int millisecond,
            int microsecond,
            TimeSpan offset) : this(year, month, day, hour, minute, second, millisecond, offset)
        {
            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond)
            {
                throw new ArgumentOutOfRangeException();
            }
            var utcTicks = checked(_utcTicks + (long)microsecond * TimeSpan.TicksPerMicrosecond);
            if ((ulong)utcTicks > (ulong)DateTime.MaxTicks)
            {
                throw new ArgumentOutOfRangeException();
            }
            _utcTicks = utcTicks;
        }

        public DateTimeOffset(DateOnly date, TimeOnly time, TimeSpan offset) :
            this(date.ToDateTime(time), offset)
        {
        }

        public static DateTimeOffset UtcNow
        {
            get { return new DateTimeOffset(DateTime.UtcNow); }
        }

        public static DateTimeOffset Now
        {
            get { return UtcNow.ToLocalTime(); }
        }

        public DateTime DateTime { get { return new(Ticks, DateTimeKind.Unspecified); } }
        public DateTime UtcDateTime { get { return new(_utcTicks, DateTimeKind.Utc); } }
        public DateTime LocalDateTime
        {
            get { return new DateTime(Ticks, DateTimeKind.Local); }
        }
        public DateTimeOffset ToLocalTime()
        {
            PlatformServices.EnsureLocalTimeReady();
            var offset = PlatformServices.LocalTimeOffsets.Resolve(
                _utcTicks,
                LocalTimeBasis.Utc);
            var localTicks = _utcTicks + offset.Ticks;
            if ((ulong)localTicks > (ulong)DateTime.MaxTicks)
            {
                localTicks = localTicks < 0 ? 0 : DateTime.MaxTicks;
            }
            return new DateTimeOffset(localTicks, new TimeSpan(offset.Ticks));
        }

        private static TimeSpan ResolveLocalOffset(long ticks)
        {
            PlatformServices.EnsureLocalTimeReady();
            return new TimeSpan(PlatformServices.LocalTimeOffsets.Resolve(
                ticks,
                LocalTimeBasis.Local).Ticks);
        }
        public DateTimeOffset ToUniversalTime() => new(_utcTicks, TimeSpan.Zero);
        public DateTime Date { get { return DateTime.Date; } }
        public DayOfWeek DayOfWeek { get { return DateTime.DayOfWeek; } }
        public int Day { get { return DateTime.Day; } }
        public int DayOfYear { get { return GetDayOfYear(DateTime); } }
        public int Hour { get { return DateTime.Hour; } }
        public int Minute { get { return DateTime.Minute; } }
        public int Second { get { return DateTime.Second; } }
        public int Millisecond { get { return DateTime.Millisecond; } }
        public int Microsecond { get { return (int)(Ticks / TimeSpan.TicksPerMicrosecond % TimeSpan.MicrosecondsPerMillisecond); } }
        public int Nanosecond { get { return (int)(Ticks % TimeSpan.TicksPerMicrosecond * TimeSpan.NanosecondsPerTick); } }
        public int Month { get { return DateTime.Month; } }
        public int Year { get { return DateTime.Year; } }
        public TimeSpan TimeOfDay { get { return DateTime.TimeOfDay; } }
        public TimeSpan Offset { get { return TimeSpan.FromMinutes(_offsetMinutes); } }
        public int TotalOffsetMinutes { get { return _offsetMinutes; } }
        public long Ticks { get { return checked(_utcTicks + Offset.Ticks); } }
        public long UtcTicks { get { return _utcTicks; } }

        public DateTimeOffset Add(TimeSpan value) => new(checked(Ticks + value.Ticks), Offset);
        public DateTimeOffset AddDays(double value) => Add(DateTime.AddDays(value));
        public DateTimeOffset AddHours(double value) => Add(DateTime.AddHours(value));
        public DateTimeOffset AddMinutes(double value) => Add(DateTime.AddMinutes(value));
        public DateTimeOffset AddSeconds(double value) => Add(DateTime.AddSeconds(value));
        public DateTimeOffset AddMilliseconds(double value) => Add(DateTime.AddMilliseconds(value));
        public DateTimeOffset AddMicroseconds(double value) => Add(DateTime.Add(TimeSpan.FromMicroseconds(value)));
        public DateTimeOffset AddMonths(int value) => Add(DateTime.AddMonths(value));
        public DateTimeOffset AddYears(int value) => Add(DateTime.AddYears(value));
        public DateTimeOffset AddTicks(long value) => Add(DateTime.AddTicks(value));
        public DateTimeOffset ToOffset(TimeSpan offset) => new(checked(_utcTicks + offset.Ticks), offset);
        public DateTimeOffset Subtract(TimeSpan value) => Add(-value);
        public TimeSpan Subtract(DateTimeOffset value) => new(checked(_utcTicks - value._utcTicks));

        public static int Compare(DateTimeOffset left, DateTimeOffset right) => left._utcTicks.CompareTo(right._utcTicks);
        public int CompareTo(DateTimeOffset value) => Compare(this, value);
        public int CompareTo(object? value) => value is null ? 1
            : value is DateTimeOffset other ? CompareTo(other) : throw new ArgumentException();
        public bool Equals(DateTimeOffset value) => _utcTicks == value._utcTicks;
        public bool EqualsExact(DateTimeOffset value) => Equals(value) && _offsetMinutes == value._offsetMinutes;
        public static bool Equals(DateTimeOffset left, DateTimeOffset right) => left == right;
        public override bool Equals(object? value) => value is DateTimeOffset other && Equals(other);
        public override int GetHashCode() => _utcTicks.GetHashCode();

        public static DateTimeOffset FromFileTime(long fileTime) => throw new PlatformNotSupportedException(
            "File-time conversion to local time zones is unavailable in the invariant profile.");

        public static DateTimeOffset FromUnixTimeSeconds(long seconds) =>
            FromUnixTime(seconds, TimeSpan.TicksPerSecond);

        public static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds) =>
            FromUnixTime(milliseconds, TimeSpan.TicksPerMillisecond);

        public long ToFileTime() => checked(_utcTicks + FileTimeOffsetTicks);

        public long ToUnixTimeSeconds() =>
            (long)((ulong)_utcTicks / TimeSpan.TicksPerSecond)
                - UnixEpochTicks / TimeSpan.TicksPerSecond;

        public long ToUnixTimeMilliseconds() =>
            (long)((ulong)_utcTicks / TimeSpan.TicksPerMillisecond)
                - UnixEpochTicks / TimeSpan.TicksPerMillisecond;

        public override string ToString() => DateTime.ToString() + FormatOffset();
        public string ToString(string? format) => FormatInvariant(format);
        public string ToString(IFormatProvider? formatProvider) => ToString();
        public string ToString(string? format, IFormatProvider? formatProvider) => FormatInvariant(format);

        public bool TryFormat(Span<char> destination, out int charsWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? formatProvider = null) =>
            TryCopy(FormatInvariant(format.Length == 0 ? null : string.Create(format.ToArray())), destination, out charsWritten);

        public bool TryFormat(Span<byte> destination, out int bytesWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? formatProvider = null)
        {
            var text = FormatInvariant(format.Length == 0 ? null : string.Create(format.ToArray()));
            if (destination.Length < text.Length)
            {
                bytesWritten = 0;
                return false;
            }
            for (var index = 0; index < text.Length; index++) destination[index] = (byte)text[index];
            bytesWritten = text.Length;
            return true;
        }

        public static DateTimeOffset Parse(string value)
        {
            if (value is null) throw new ArgumentNullException();
            return TryParse(value, out var result) ? result : throw new FormatException();
        }

        public static DateTimeOffset Parse(string value, IFormatProvider? formatProvider) => Parse(value);
        public static DateTimeOffset Parse(string value, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles)
        {
            ValidateStyles(styles);
            var result = Parse(value, formatProvider);
            return (styles & System.Globalization.DateTimeStyles.AdjustToUniversal) != 0
                ? result.ToUniversalTime()
                : result;
        }

        public static DateTimeOffset Parse(ReadOnlySpan<char> value, IFormatProvider? formatProvider) =>
            Parse(string.Create(value.ToArray()));
        public static DateTimeOffset Parse(ReadOnlySpan<char> value, IFormatProvider? formatProvider = null,
            System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.None)
        {
            ValidateStyles(styles);
            var result = Parse(value, formatProvider);
            return (styles & System.Globalization.DateTimeStyles.AdjustToUniversal) != 0
                ? result.ToUniversalTime()
                : result;
        }

        public static bool TryParse(string? value, out DateTimeOffset result)
        {
            result = default;
            if (value is null) return false;
            var start = 0;
            var end = value.Length;
            while (start < end && IsWhiteSpace(value[start])) start++;
            while (end > start && IsWhiteSpace(value[end - 1])) end--;
            if (start != 0 || end != value.Length)
            {
                value = value.Substring(start, end - start);
            }
            if (value.Length < 25) return false;
            var offsetStart = value.Length - 6;
            var sign = value[offsetStart];
            if ((sign != '+' && sign != '-') ||
                !InvariantDateTimeText.TryReadDigits(value, offsetStart + 1, 2, out var hours) ||
                !InvariantDateTimeText.HasSeparator(value, offsetStart + 3, ':') ||
                !InvariantDateTimeText.TryReadDigits(value, offsetStart + 4, 2, out var minutes) ||
                hours > 14 || minutes >= 60 || (hours == 14 && minutes != 0))
            {
                return false;
            }
            var dateText = value.Substring(0, offsetStart);
            if (!DateTime.TryParse(dateText, out var dateTime) || dateTime.Kind != DateTimeKind.Unspecified)
            {
                return false;
            }
            try
            {
                var offsetMinutes = hours * 60 + minutes;
                if (sign == '-') offsetMinutes = -offsetMinutes;
                result = new DateTimeOffset(dateTime.Ticks, TimeSpan.FromMinutes(offsetMinutes));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryParse(string? value, IFormatProvider? formatProvider, out DateTimeOffset result) =>
            TryParse(value, out result);
        public static bool TryParse(string? value, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles, out DateTimeOffset result)
        {
            ValidateStyles(styles);
            if (!TryParse(value, out result)) return false;
            if ((styles & System.Globalization.DateTimeStyles.AdjustToUniversal) != 0)
            {
                result = result.ToUniversalTime();
            }
            return true;
        }

        public static bool TryParse(ReadOnlySpan<char> value, out DateTimeOffset result) =>
            TryParse(string.Create(value.ToArray()), out result);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? formatProvider, out DateTimeOffset result) =>
            TryParse(value, out result);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles, out DateTimeOffset result)
        {
            ValidateStyles(styles);
            if (!TryParse(value, out result)) return false;
            if ((styles & System.Globalization.DateTimeStyles.AdjustToUniversal) != 0)
            {
                result = result.ToUniversalTime();
            }
            return true;
        }

        public static DateTimeOffset ParseExact(string value, string format, IFormatProvider? formatProvider) =>
            TryParseExact(value, format, formatProvider, out var result) ? result : throw new FormatException();

        public static DateTimeOffset ParseExact(string value, string format, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles) =>
            TryParseExact(value, format, formatProvider, styles, out var result) ? result : throw new FormatException();

        public static DateTimeOffset ParseExact(string value, string[] formats, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles) =>
            TryParseExact(value, formats, formatProvider, styles, out var result) ? result : throw new FormatException();

        public static bool TryParseExact(string? value, string? format, IFormatProvider? formatProvider,
            out DateTimeOffset result)
        {
            return TryParseExact(value, format, formatProvider, System.Globalization.DateTimeStyles.None, out result);
        }

        public static bool TryParseExact(string? value, string? format, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles, out DateTimeOffset result)
        {
            ValidateStyles(styles);
            if (value is null || format is null || !IsRoundTripFormat(format))
            {
                result = default;
                return false;
            }
            return TryParse(value, formatProvider, styles, out result);
        }

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider = null) =>
            ParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), formatProvider);

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider, out DateTimeOffset result) =>
            TryParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), formatProvider, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider, System.Globalization.DateTimeStyles styles, out DateTimeOffset result) =>
            TryParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), formatProvider, styles, out result);

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider, System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.None) =>
            TryParseExact(value, format, formatProvider, styles, out var result) ? result : throw new FormatException();

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> value, string[] formats,
            IFormatProvider? formatProvider, System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.None) =>
            TryParseExact(value, formats, formatProvider, styles, out var result) ? result : throw new FormatException();

        public static DateTimeOffset ParseExact(string value, string[] formats, IFormatProvider? formatProvider) =>
            ParseExact(value, formats, formatProvider, System.Globalization.DateTimeStyles.None);

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? formatProvider,
            System.Globalization.DateTimeStyles styles, out DateTimeOffset result)
        {
            ValidateStyles(styles);
            result = default;
            if (value is null || formats is null) return false;
            for (var index = 0; index < formats.Length; index++)
            {
                if (TryParseExact(value, formats[index], formatProvider, styles, out result)) return true;
            }
            return false;
        }

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? formatProvider,
            out DateTimeOffset result) =>
            TryParseExact(value, formats, formatProvider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            IFormatProvider? formatProvider, System.Globalization.DateTimeStyles styles, out DateTimeOffset result)
        {
            ValidateStyles(styles);
            return TryParseExact(string.Create(value.ToArray()), formats, formatProvider, styles, out result);
        }

        public static implicit operator DateTimeOffset(DateTime value) => new(value);
        public static DateTimeOffset operator +(DateTimeOffset value, TimeSpan span) => value.Add(span);
        public static DateTimeOffset operator -(DateTimeOffset value, TimeSpan span) => value.Subtract(span);
        public static TimeSpan operator -(DateTimeOffset left, DateTimeOffset right) => left.Subtract(right);
        public static bool operator ==(DateTimeOffset left, DateTimeOffset right) => left.Equals(right);
        public static bool operator !=(DateTimeOffset left, DateTimeOffset right) => !left.Equals(right);
        public static bool operator <(DateTimeOffset left, DateTimeOffset right) => left._utcTicks < right._utcTicks;
        public static bool operator <=(DateTimeOffset left, DateTimeOffset right) => left._utcTicks <= right._utcTicks;
        public static bool operator >(DateTimeOffset left, DateTimeOffset right) => left._utcTicks > right._utcTicks;
        public static bool operator >=(DateTimeOffset left, DateTimeOffset right) => left._utcTicks >= right._utcTicks;

        public void Deconstruct(out DateOnly date, out TimeOnly time, out TimeSpan offset)
        {
            date = DateOnly.FromDateTime(DateTime);
            time = TimeOnly.FromDateTime(DateTime);
            offset = Offset;
        }

        private static DateTimeOffset FromUnixTime(long value, long ticksPerUnit)
        {
            try
            {
                return new DateTimeOffset(checked(UnixEpochTicks + checked(value * ticksPerUnit)), TimeSpan.Zero);
            }
            catch (Exception)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private DateTimeOffset Add(DateTime value) => new(value.Ticks, Offset);

        private static int ValidateOffset(TimeSpan offset)
        {
            if (offset.Ticks % TimeSpan.TicksPerMinute != 0 ||
                offset.Ticks < -MaxOffsetMinutes * TimeSpan.TicksPerMinute ||
                offset.Ticks > MaxOffsetMinutes * TimeSpan.TicksPerMinute)
            {
                throw new ArgumentException();
            }
            return (int)(offset.Ticks / TimeSpan.TicksPerMinute);
        }

        private string FormatInvariant(string? format)
        {
            if (format is not null && format.Length != 0 && format != "O" && format != "o" &&
                format != "G" && format != "g")
            {
                throw new FormatException();
            }
            return format is "O" or "o"
                ? DateTime.ToString("O") + FormatOffset()
                : ToString();
        }

        private string FormatOffset()
        {
            var negative = _offsetMinutes < 0;
            var minutes = negative ? -_offsetMinutes : _offsetMinutes;
            return (negative ? "-" : "+")
                + InvariantDateTimeText.TwoDigits(minutes / 60) + ":"
                + InvariantDateTimeText.TwoDigits(minutes % 60);
        }

        private static int GetDayOfYear(DateTime value)
        {
            var days = 0;
            for (var month = 1; month < value.Month; month++) days += DateTime.DaysInMonth(value.Year, month);
            return days + value.Day;
        }

        private static bool IsRoundTripFormat(string format) => format == "O" || format == "o" || format == "G" || format == "g";

        private static bool IsWhiteSpace(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        private static void ValidateStyles(System.Globalization.DateTimeStyles styles)
        {
            const System.Globalization.DateTimeStyles supported =
                System.Globalization.DateTimeStyles.AllowLeadingWhite |
                System.Globalization.DateTimeStyles.AllowTrailingWhite |
                System.Globalization.DateTimeStyles.AllowInnerWhite |
                System.Globalization.DateTimeStyles.NoCurrentDateDefault |
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeLocal |
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.RoundtripKind;
            const System.Globalization.DateTimeStyles localUniversal =
                System.Globalization.DateTimeStyles.AssumeLocal |
                System.Globalization.DateTimeStyles.AssumeUniversal;
            if ((styles & ~supported) != 0 || (styles & localUniversal) == localUniversal ||
                (styles & System.Globalization.DateTimeStyles.NoCurrentDateDefault) != 0)
            {
                throw new ArgumentException();
            }
        }

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

namespace System.Globalization
{
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    [Flags]
    public enum DateTimeStyles
    {
        None = 0x00000000,
        AllowLeadingWhite = 0x00000001,
        AllowTrailingWhite = 0x00000002,
        AllowInnerWhite = 0x00000004,
        AllowWhiteSpaces = AllowLeadingWhite | AllowInnerWhite | AllowTrailingWhite,
        NoCurrentDateDefault = 0x00000008,
        AdjustToUniversal = 0x00000010,
        AssumeLocal = 0x00000020,
        AssumeUniversal = 0x00000040,
        RoundtripKind = 0x00000080,
    }
}
