// Adapted from dotnet/runtime System.Private.CoreLib DateTimeOffset.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using Runtime.InteropServices;
    using Runtime.InteropServices.TimeZones;

    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct DateTimeOffset : IComparable, IComparable<DateTimeOffset>, IEquatable<DateTimeOffset>,
        IFormattable, IParsable<DateTimeOffset>, ISpanFormattable, ISpanParsable<DateTimeOffset>,
        IUtf8SpanFormattable
    {
        private const int MaxOffsetMinutes = 14 * 60;
        internal const long MaxOffset = MaxOffsetMinutes * TimeSpan.TicksPerMinute;
        internal const long MinOffset = -MaxOffset;
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
                    dateTime.IsAmbiguousDaylightSavingTime()
                        ? LocalTimeBasis.LocalAmbiguousDaylight
                        : LocalTimeBasis.Local);
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
                : ResolveLocalOffset(dateTime))
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

        // Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce. NetWasm's clock
        // has no leap seconds, so upstream's leap-second branch never applies.
        public DateTimeOffset(int year, int month, int day, int hour, int minute, int second, int millisecond, System.Globalization.Calendar calendar, TimeSpan offset)
            : this(CalendarTicks(calendar, year, month, day, hour, minute, second, millisecond), offset)
        {
        }

        private static long CalendarTicks(System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute, int second, int millisecond)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            return calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).Ticks;
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
            get { return UtcDateTime.ToLocalTime(); }
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

        private static TimeSpan ResolveLocalOffset(DateTime dateTime)
        {
            PlatformServices.EnsureLocalTimeReady();
            return new TimeSpan(PlatformServices.LocalTimeOffsets.Resolve(
                dateTime.Ticks,
                dateTime.IsAmbiguousDaylightSavingTime()
                    ? LocalTimeBasis.LocalAmbiguousDaylight
                    : LocalTimeBasis.Local).Ticks);
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
        public DateTimeOffset Subtract(TimeSpan value) => Add(DateTime.Subtract(value));
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

        public static DateTimeOffset FromFileTime(long fileTime) =>
            new(DateTime.FromFileTimeUtc(fileTime).ToLocalTime());

        public static DateTimeOffset FromUnixTimeSeconds(long seconds) =>
            FromUnixTime(seconds, TimeSpan.TicksPerSecond);

        public static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds) =>
            FromUnixTime(milliseconds, TimeSpan.TicksPerMillisecond);

        public long ToFileTime() => UtcDateTime.ToFileTimeUtc();

        public long ToUnixTimeSeconds() =>
            (long)((ulong)_utcTicks / TimeSpan.TicksPerSecond)
                - UnixEpochTicks / TimeSpan.TicksPerSecond;

        public long ToUnixTimeMilliseconds() =>
            (long)((ulong)_utcTicks / TimeSpan.TicksPerMillisecond)
                - UnixEpochTicks / TimeSpan.TicksPerMillisecond;

        // Text members ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
        // NetWasm: see InvariantDateTimeDefaults.
        public override string ToString() =>
            DateTimeFormatInfo.s_currentInfo is null
                ? InvariantDateTimeDefaults.General(ClockDateTime, Offset)
                : DateTimeFormat.Format(ClockDateTime, null, null, Offset);

        public string ToString([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string? format) =>
            DateTimeFormat.Format(ClockDateTime, format, null, Offset);

        public string ToString(IFormatProvider? formatProvider) =>
            DateTimeFormat.Format(ClockDateTime, null, formatProvider, Offset);

        public string ToString([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string? format, IFormatProvider? formatProvider) =>
            DateTimeFormat.Format(ClockDateTime, format, formatProvider, Offset);

        public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = null) =>
            DateTimeFormat.TryFormat(ClockDateTime, destination, out charsWritten, format, formatProvider, Offset);

        /// <inheritdoc cref="IUtf8SpanFormattable.TryFormat" />
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = null) =>
            DateTimeFormat.TryFormat(ClockDateTime, utf8Destination, out bytesWritten, format, formatProvider, Offset);


        // Constructs a DateTimeOffset from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTimeOffset Parse(string input)
        {
            if (input == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);

            DateTime dateResult = DateTimeParse.Parse(input,
                                                      DateTimeFormatInfo.CurrentInfo,
                                                      DateTimeStyles.None,
                                                      out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        // Constructs a DateTimeOffset from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTimeOffset Parse(string input, IFormatProvider? formatProvider)
            => Parse(input, formatProvider, DateTimeStyles.None);

        public static DateTimeOffset Parse(string input, IFormatProvider? formatProvider, DateTimeStyles styles)
        {
            styles = ValidateStyles(styles);
            if (input == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);

            DateTime dateResult = DateTimeParse.Parse(input,
                                                      DateTimeFormatInfo.GetInstance(formatProvider),
                                                      styles,
                                                      out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset Parse(ReadOnlySpan<char> input, IFormatProvider? formatProvider = null, DateTimeStyles styles = DateTimeStyles.None)
        {
            styles = ValidateStyles(styles);
            DateTime dateResult = DateTimeParse.Parse(input, DateTimeFormatInfo.GetInstance(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        // Constructs a DateTimeOffset from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTimeOffset ParseExact(string input, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format, IFormatProvider? formatProvider)
            => ParseExact(input, format, formatProvider, DateTimeStyles.None);

        // Constructs a DateTimeOffset from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTimeOffset ParseExact(string input, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format, IFormatProvider? formatProvider, DateTimeStyles styles)
        {
            styles = ValidateStyles(styles);
            if (input == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            if (format == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.format);

            DateTime dateResult = DateTimeParse.ParseExact(input,
                                                           format,
                                                           DateTimeFormatInfo.GetInstance(formatProvider),
                                                           styles,
                                                           out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format, IFormatProvider? formatProvider, DateTimeStyles styles = DateTimeStyles.None)
        {
            styles = ValidateStyles(styles);
            DateTime dateResult = DateTimeParse.ParseExact(input, format, DateTimeFormatInfo.GetInstance(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(string input, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string[] formats, IFormatProvider? formatProvider, DateTimeStyles styles)
        {
            styles = ValidateStyles(styles);
            if (input == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);

            DateTime dateResult = DateTimeParse.ParseExactMultiple(input,
                                                                   formats,
                                                                   DateTimeFormatInfo.GetInstance(formatProvider),
                                                                   styles,
                                                                   out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string[] formats, IFormatProvider? formatProvider, DateTimeStyles styles = DateTimeStyles.None)
        {
            styles = ValidateStyles(styles);
            DateTime dateResult = DateTimeParse.ParseExactMultiple(input, formats, DateTimeFormatInfo.GetInstance(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static bool TryParse([NotNullWhen(true)] string? input, out DateTimeOffset result)
        {
            bool parsed = DateTimeParse.TryParse(input,
                                                    DateTimeFormatInfo.CurrentInfo,
                                                    DateTimeStyles.None,
                                                    out DateTime dateResult,
                                                    out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParse(ReadOnlySpan<char> input, out DateTimeOffset result)
        {
            bool parsed = DateTimeParse.TryParse(input, DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParse([NotNullWhen(true)] string? input, IFormatProvider? formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            if (input == null)
            {
                result = default;
                return false;
            }

            bool parsed = DateTimeParse.TryParse(input,
                                                    DateTimeFormatInfo.GetInstance(formatProvider),
                                                    styles,
                                                    out DateTime dateResult,
                                                    out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParse(ReadOnlySpan<char> input, IFormatProvider? formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            bool parsed = DateTimeParse.TryParse(input, DateTimeFormatInfo.GetInstance(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string? format, IFormatProvider? formatProvider, DateTimeStyles styles,
                                            out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            if (input == null || format == null)
            {
                result = default;
                return false;
            }

            bool parsed = DateTimeParse.TryParseExact(input,
                                                         format,
                                                         DateTimeFormatInfo.GetInstance(formatProvider),
                                                         styles,
                                                         out DateTime dateResult,
                                                         out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact(
            ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format, IFormatProvider? formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            bool parsed = DateTimeParse.TryParseExact(input, format, DateTimeFormatInfo.GetInstance(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string?[]? formats, IFormatProvider? formatProvider, DateTimeStyles styles,
                                            out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            if (input == null)
            {
                result = default;
                return false;
            }

            bool parsed = DateTimeParse.TryParseExactMultiple(input,
                                                                 formats,
                                                                 DateTimeFormatInfo.GetInstance(formatProvider),
                                                                 styles,
                                                                 out DateTime dateResult,
                                                                 out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact(
            ReadOnlySpan<char> input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string?[]? formats, IFormatProvider? formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            bool parsed = DateTimeParse.TryParseExactMultiple(input, formats, DateTimeFormatInfo.GetInstance(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        private static DateTimeStyles ValidateStyles(DateTimeStyles styles)
        {
            const DateTimeStyles localUniversal = DateTimeStyles.AssumeLocal | DateTimeStyles.AssumeUniversal;

            if ((styles & (DateTimeFormatInfo.InvalidDateTimeStyles | DateTimeStyles.NoCurrentDateDefault)) != 0
                || (styles & localUniversal) == localUniversal)
            {
                ThrowInvalid(styles);
            }

            // RoundtripKind does not make sense for DateTimeOffset; ignore this flag for backward compatibility with DateTime
            // AssumeLocal is also ignored as that is what we do by default with DateTimeOffset.Parse
            return styles & (~DateTimeStyles.RoundtripKind & ~DateTimeStyles.AssumeLocal);

            static void ThrowInvalid(DateTimeStyles styles)
            {
                string message = (styles & DateTimeFormatInfo.InvalidDateTimeStyles) != 0 ? SR.Argument_InvalidDateTimeStyles
                    : (styles & localUniversal) == localUniversal ? SR.Argument_ConflictingDateTimeStyles
                    : SR.Argument_DateTimeOffsetInvalidDateTimeStyles;
                throw new ArgumentException(message, nameof(styles));
            }
        }


        //
        // IParsable
        //

        /// <inheritdoc cref="IParsable{TSelf}.TryParse(string?, IFormatProvider?, out TSelf)" />
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out DateTimeOffset result) => TryParse(s, provider, DateTimeStyles.None, out result);

        //
        // ISpanParsable
        //

        /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)" />
        public static DateTimeOffset Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, provider, DateTimeStyles.None);

        /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse(ReadOnlySpan{char}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeOffset result) => TryParse(s, provider, DateTimeStyles.None, out result);

        // NetWasm: upstream's clock time and offset validation over NetWasm's UTC ticks and offset; the
        // constructor validates the offset and both the clock and UTC times as upstream's CreateValidateOffset does.
        private DateTime ClockDateTime => new DateTime(Ticks);

        private static DateTimeOffset CreateValidateOffset(DateTime dateTime, TimeSpan offset) =>
            new DateTimeOffset(dateTime.Ticks, offset);


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

        private static int GetDayOfYear(DateTime value)
        {
            var days = 0;
            for (var month = 1; month < value.Month; month++) days += DateTime.DaysInMonth(value.Year, month);
            return days + value.Day;
        }

        private static int ValidateOffset(TimeSpan offset)
        {
            if (offset.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                throw new ArgumentException();
            }
            if (offset.Ticks < -MaxOffsetMinutes * TimeSpan.TicksPerMinute ||
                offset.Ticks > MaxOffsetMinutes * TimeSpan.TicksPerMinute)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            return (int)(offset.Ticks / TimeSpan.TicksPerMinute);
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
