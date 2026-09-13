// Adapted from dotnet/runtime System.Private.CoreLib DateTime.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using Runtime.InteropServices;
    using Runtime.InteropServices.TimeZones;

    public readonly struct DateTime : IComparable, IComparable<DateTime>, IEquatable<DateTime>,
        IFormattable, IParsable<DateTime>, ISpanFormattable, ISpanParsable<DateTime>,
        IUtf8SpanFormattable
    {
        private readonly long _ticks;
        private readonly DateTimeKind _kind;

        public DateTime(long ticks) : this(ticks, DateTimeKind.Unspecified)
        {
        }

        public DateTime(long ticks, DateTimeKind kind)
        {
            if ((ulong)ticks > (ulong)MaxTicks)
            {
                throw new ArgumentOutOfRangeException();
            }
            if ((uint)kind > (uint)DateTimeKind.Local)
            {
                throw new ArgumentException();
            }
            _ticks = ticks;
            _kind = kind;
        }

        public DateTime(DateOnly date, TimeOnly time) : this(
            checked((long)date.DayNumber * TimeSpan.TicksPerDay + time.Ticks),
            DateTimeKind.Unspecified)
        {
        }

        public DateTime(DateOnly date, TimeOnly time, DateTimeKind kind) : this(
            checked((long)date.DayNumber * TimeSpan.TicksPerDay + time.Ticks),
            kind)
        {
        }

        public DateTime(int year, int month, int day) :
            this(year, month, day, 0, 0, 0, DateTimeKind.Unspecified)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second) :
            this(year, month, day, hour, minute, second, DateTimeKind.Unspecified)
        {
        }

        public DateTime(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            int millisecond) : this(
                year,
                month,
                day,
                hour,
                minute,
                second,
                millisecond,
                DateTimeKind.Unspecified)
        {
        }

        public DateTime(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            int millisecond,
            DateTimeKind kind) : this(
                checked((long)GregorianDateMath.DateToDayNumber(year, month, day)
                    * TimeSpan.TicksPerDay + TimeToTicks(hour, minute, second)
                    + ValidateMillisecond(millisecond) * TimeSpan.TicksPerMillisecond),
                kind)
        {
        }

        public DateTime(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            int millisecond,
            int microsecond) : this(
                year, month, day, hour, minute, second, millisecond, microsecond,
                DateTimeKind.Unspecified)
        {
        }

        public DateTime(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            int millisecond,
            int microsecond,
            DateTimeKind kind) : this(
                checked((long)GregorianDateMath.DateToDayNumber(year, month, day)
                    * TimeSpan.TicksPerDay + TimeToTicks(hour, minute, second, millisecond, microsecond)),
                kind)
        {
        }

        public DateTime(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            DateTimeKind kind) : this(
                checked((long)GregorianDateMath.DateToDayNumber(year, month, day)
                    * TimeSpan.TicksPerDay + TimeToTicks(hour, minute, second)),
                kind)
        {
        }

        public const long MaxTicks = GregorianDateMath.DaysTo10000 * TimeSpan.TicksPerDay - 1;
        private const long DoubleDateOffset = 599264352000000000L;
        private const long FileTimeOffsetTicks = 504911232000000000L;
        private const long OADateMinAsTicks = (36524 - 365) * TimeSpan.TicksPerDay;
        private const double OADateMinAsDouble = -657435.0;
        private const double OADateMaxAsDouble = 2958466.0;
        private const long MillisecondsPerDay = TimeSpan.TicksPerDay / TimeSpan.TicksPerMillisecond;
        public static readonly DateTime MinValue = new(0);
        public static readonly DateTime MaxValue = new(MaxTicks);
        public static readonly DateTime UnixEpoch = new(
            GregorianDateMath.UnixEpochTicks, DateTimeKind.Utc);
        public static DateTime UtcNow
        {
            get => new(DateTimeClock.UtcNowTicks(), DateTimeKind.Utc);
        }

        public static DateTime Today
        {
            get => Now.Date;
        }
        public static DateTime Now
        {
            get => UtcNow.ToLocalTime();
        }

        public bool IsDaylightSavingTime() => _kind != DateTimeKind.Utc &&
            ResolveLocalTimeOffset(_ticks, LocalTimeBasis.Local)
                .IsDaylightSavingTime;

        public DateTime ToLocalTime()
        {
            if (_kind == DateTimeKind.Local)
            {
                return this;
            }
            var offset = ResolveLocalTimeOffset(_ticks, LocalTimeBasis.Utc);
            var localTicks = _ticks + offset.Ticks;
            if ((ulong)localTicks > (ulong)MaxTicks)
            {
                localTicks = localTicks < 0 ? 0 : MaxTicks;
            }
            return new DateTime(localTicks, DateTimeKind.Local);
        }

        public DateTime ToUniversalTime()
        {
            if (_kind == DateTimeKind.Utc)
            {
                return this;
            }
            var offset = ResolveLocalTimeOffset(_ticks, LocalTimeBasis.Local);
            return new DateTime(checked(_ticks - offset.Ticks), DateTimeKind.Utc);
        }

        private static LocalTimeOffset ResolveLocalTimeOffset(
            long ticks,
            LocalTimeBasis basis)
        {
            PlatformServices.EnsureLocalTimeReady();
            return PlatformServices.LocalTimeOffsets.Resolve(ticks, basis);
        }
        public long Ticks { get { return _ticks; } }
        public DateTimeKind Kind { get { return _kind; } }
        public DateTime Date { get { return new(_ticks - _ticks % TimeSpan.TicksPerDay, _kind); } }
        public TimeSpan TimeOfDay { get { return new(_ticks % TimeSpan.TicksPerDay); } }
        public int Year { get { GetDateParts(out var year, out _, out _); return year; } }
        public int Month { get { GetDateParts(out _, out var month, out _); return month; } }
        public int Day { get { GetDateParts(out _, out _, out var day); return day; } }
        public int DayOfYear
        {
            get
            {
                GetDateParts(out var year, out _, out var day);
                return GregorianDateMath.DateToDayNumber(year, Month, day)
                    - GregorianDateMath.DateToDayNumber(year, 1, 1) + 1;
            }
        }
        public int Hour { get { return (int)(_ticks / TimeSpan.TicksPerHour % 24); } }
        public int Minute { get { return (int)(_ticks / TimeSpan.TicksPerMinute % 60); } }
        public int Second { get { return (int)(_ticks / TimeSpan.TicksPerSecond % 60); } }
        public int Millisecond { get { return (int)(_ticks / TimeSpan.TicksPerMillisecond % 1000); } }
        public int Microsecond { get { return (int)(_ticks / TimeSpan.TicksPerMicrosecond % 1000); } }
        public int Nanosecond { get { return (int)(_ticks % TimeSpan.TicksPerMicrosecond) * 100; } }
        public DayOfWeek DayOfWeek { get { return (DayOfWeek)((_ticks / TimeSpan.TicksPerDay + 1) % 7); } }

        public DateTime Add(TimeSpan value) => AddTicks(value.Ticks);
        public DateTime AddTicks(long value)
        {
            var ticks = unchecked(_ticks + value);
            if ((ulong)ticks > (ulong)MaxTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new DateTime(ticks, _kind);
        }
        public DateTime AddDays(double value) => AddUnits(
            value, MaxTicks / TimeSpan.TicksPerDay, TimeSpan.TicksPerDay);
        public DateTime AddHours(double value) => AddUnits(
            value, MaxTicks / TimeSpan.TicksPerHour, TimeSpan.TicksPerHour);
        public DateTime AddMinutes(double value) => AddUnits(
            value, MaxTicks / TimeSpan.TicksPerMinute, TimeSpan.TicksPerMinute);
        public DateTime AddSeconds(double value) => AddUnits(
            value, MaxTicks / TimeSpan.TicksPerSecond, TimeSpan.TicksPerSecond);
        public DateTime AddMilliseconds(double value) => AddUnits(
            value, MaxTicks / TimeSpan.TicksPerMillisecond, TimeSpan.TicksPerMillisecond);
        public DateTime AddMicroseconds(double value) => AddUnits(
            value, MaxTicks / TimeSpan.TicksPerMicrosecond, TimeSpan.TicksPerMicrosecond);
        public DateTime AddMonths(int value)
        {
            var date = new DateOnly(Year, Month, Day).AddMonths(value);
            return new DateTime(
                checked((long)date.DayNumber * TimeSpan.TicksPerDay + TimeOfDay.Ticks),
                _kind);
        }
        public DateTime AddYears(int value)
        {
            if (value < -10000 || value > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return AddMonths(value * 12);
        }
        public TimeSpan Subtract(DateTime value) => new(_ticks - value._ticks);
        public DateTime Subtract(TimeSpan value) => AddTicks(checked(-value.Ticks));
        public static bool IsLeapYear(int year) => GregorianDateMath.IsLeapYear(year);
        public static int DaysInMonth(int year, int month) => DateOnly.DaysInMonth(year, month);

        public static DateTime FromOADate(double value) =>
            new(DoubleDateToTicks(value), DateTimeKind.Unspecified);

        public double ToOADate() => TicksToOADate(Ticks);

        public long ToBinary()
        {
            if (_kind == DateTimeKind.Local)
            {
                throw new PlatformNotSupportedException(
                    "Local time-zone conversion is unavailable in the invariant profile.");
            }
            return _ticks | ((long)_kind << 62);
        }

        public static DateTime FromBinary(long data)
        {
            var kindBits = (ulong)data >> 62;
            if (kindBits > 1)
            {
                throw new ArgumentException();
            }
            var ticks = (long)((ulong)data & 0x3fff_ffff_ffff_ffffUL);
            return new DateTime(ticks, kindBits == 1 ? DateTimeKind.Utc : DateTimeKind.Unspecified);
        }

        public long ToFileTimeUtc()
        {
            var fileTime = checked(_ticks - FileTimeOffsetTicks);
            if (fileTime < 0) throw new ArgumentOutOfRangeException();
            return fileTime;
        }

        public static DateTime FromFileTimeUtc(long fileTime)
        {
            if (fileTime < 0) throw new ArgumentOutOfRangeException(nameof(fileTime));
            return new DateTime(checked(fileTime + FileTimeOffsetTicks), DateTimeKind.Utc);
        }

        public long ToFileTime() => _kind == DateTimeKind.Local
            ? throw new PlatformNotSupportedException(
                "Local time-zone conversion is unavailable in the invariant profile.")
            : ToFileTimeUtc();

        public static DateTime FromFileTime(long fileTime) => FromFileTimeUtc(fileTime);

        public TypeCode GetTypeCode() => TypeCode.DateTime;

        public int CompareTo(DateTime value) => _ticks.CompareTo(value._ticks);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is DateTime other
                ? CompareTo(other)
                : throw new ArgumentException();
        public static int Compare(DateTime left, DateTime right) =>
            left._ticks.CompareTo(right._ticks);
        public bool Equals(DateTime value) => _ticks == value._ticks;
        public override bool Equals(object? value) => value is DateTime other && Equals(other);
        public override int GetHashCode() => _ticks.GetHashCode();

        public static bool Equals(DateTime left, DateTime right) => left.Equals(right);

        public void Deconstruct(out int year, out int month, out int day) =>
            GetDateParts(out year, out month, out day);

        public void Deconstruct(out DateOnly date, out TimeOnly time)
        {
            date = DateOnly.FromDateTime(this);
            time = TimeOnly.FromDateTime(this);
        }

        public static DateTime SpecifyKind(DateTime value, DateTimeKind kind) =>
            new(value._ticks, kind);
        public override string ToString() => InvariantDateTimeText.FourDigits(Year) + "-"
            + InvariantDateTimeText.TwoDigits(Month) + "-"
            + InvariantDateTimeText.TwoDigits(Day) + "T"
            + InvariantDateTimeText.TwoDigits(Hour) + ":"
            + InvariantDateTimeText.TwoDigits(Minute) + ":"
            + InvariantDateTimeText.TwoDigits(Second)
            + InvariantDateTimeText.Fraction(_ticks)
            + (_kind == DateTimeKind.Utc ? "Z" : string.Empty);

        public string ToLongDateString() => Date.ToString();
        public string ToShortDateString() => Date.ToString();
        public string ToLongTimeString() => TimeOfDay.ToString();
        public string ToShortTimeString() => TimeOfDay.ToString();
        public string ToString(IFormatProvider? provider) => ToString();
        public string ToString(string? format) => FormatInvariant(format);
        public string ToString(string? format, IFormatProvider? provider) => FormatInvariant(format);

        public string[] GetDateTimeFormats() => new[] { ToString() };
        public string[] GetDateTimeFormats(IFormatProvider? provider) => GetDateTimeFormats();
        public string[] GetDateTimeFormats(char format) => GetDateTimeFormats(format, null);
        public string[] GetDateTimeFormats(char format, IFormatProvider? provider)
        {
            if (!IsSupportedFormat(format.ToString())) throw new FormatException();
            return new[] { ToString(format.ToString(), provider) };
        }

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

        public static DateTime Parse(string value)
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

        public static DateTime Parse(string value, IFormatProvider? provider) => Parse(value);

        public static DateTime Parse(string value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style)
        {
            ValidateStyles(style);
            return TryParse(value, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static DateTime Parse(ReadOnlySpan<char> value, IFormatProvider? provider) =>
            Parse(string.Create(value.ToArray()), provider);

        public static DateTime Parse(ReadOnlySpan<char> value, IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            Parse(string.Create(value.ToArray()), provider, style);

        public static bool TryParse(string? value, out DateTime result)
        {
            result = default;
            if (value == null || value.Length < 19 ||
                !InvariantDateTimeText.TryReadDigits(value, 0, 4, out var year) ||
                !InvariantDateTimeText.HasSeparator(value, 4, '-') ||
                !InvariantDateTimeText.TryReadDigits(value, 5, 2, out var month) ||
                !InvariantDateTimeText.HasSeparator(value, 7, '-') ||
                !InvariantDateTimeText.TryReadDigits(value, 8, 2, out var day) ||
                !InvariantDateTimeText.HasSeparator(value, 10, 'T') ||
                !InvariantDateTimeText.TryReadDigits(value, 11, 2, out var hour) ||
                !InvariantDateTimeText.HasSeparator(value, 13, ':') ||
                !InvariantDateTimeText.TryReadDigits(value, 14, 2, out var minute) ||
                !InvariantDateTimeText.HasSeparator(value, 16, ':') ||
                !InvariantDateTimeText.TryReadDigits(value, 17, 2, out var second))
            {
                return false;
            }

            var end = value.Length;
            var kind = DateTimeKind.Unspecified;
            if (value[end - 1] == 'Z')
            {
                kind = DateTimeKind.Utc;
                end--;
            }
            var fraction = 0L;
            if (end > 19 &&
                (!InvariantDateTimeText.HasSeparator(value, 19, '.') ||
                 !InvariantDateTimeText.TryReadFraction(value, 20, end, out fraction)))
            {
                return false;
            }
            if (end != 19 && fraction == 0 && end <= 20)
            {
                return false;
            }
            try
            {
                result = new DateTime(year, month, day, hour, minute, second, kind)
                    .AddTicks(fraction);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool TryParse(string? value, IFormatProvider? provider, out DateTime result) =>
            TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(string? value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateTime result)
        {
            ValidateStyles(style);
            value = TrimWhiteSpace(value, style);
            if (!TryParse(value, out result)) return false;
            if ((style & (System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal)) != 0 &&
                result._kind == DateTimeKind.Unspecified)
            {
                result = new DateTime(result._ticks, DateTimeKind.Utc);
            }
            return true;
        }

        public static bool TryParse(ReadOnlySpan<char> value, out DateTime result) =>
            TryParse(string.Create(value.ToArray()), out result);

        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider,
            out DateTime result) => TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateTime result) =>
            TryParse(string.Create(value.ToArray()), provider, style, out result);

        public static DateTime ParseExact(string value, string format, IFormatProvider? provider) =>
            ParseExact(value, format, provider, System.Globalization.DateTimeStyles.None);

        public static DateTime ParseExact(string value, string format, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style)
        {
            return TryParseExact(value, format, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static DateTime ParseExact(string value, string[] formats, IFormatProvider? provider) =>
            ParseExact(value, formats, provider, System.Globalization.DateTimeStyles.None);

        public static DateTime ParseExact(string value, string[] formats, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style)
        {
            return TryParseExact(value, formats, provider, style, out var result)
                ? result : throw new FormatException();
        }

        public static DateTime ParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            ParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), provider, style);

        public static DateTime ParseExact(ReadOnlySpan<char> value, string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None) =>
            ParseExact(string.Create(value.ToArray()), formats, provider, style);

        public static bool TryParseExact(string? value, string? format, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateTime result)
        {
            ValidateStyles(style);
            result = default;
            if (!IsSupportedFormat(format)) return false;
            return TryParse(value, provider, style, out result);
        }

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateTime result)
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
            IFormatProvider? provider, System.Globalization.DateTimeStyles style, out DateTime result) =>
            TryParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), provider, style, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            IFormatProvider? provider, System.Globalization.DateTimeStyles style, out DateTime result) =>
            TryParseExact(string.Create(value.ToArray()), formats, provider, style, out result);

        public static DateTime operator +(DateTime value, TimeSpan span) => value.Add(span);
        public static DateTime operator -(DateTime value, TimeSpan span) => value.Subtract(span);
        public static TimeSpan operator -(DateTime left, DateTime right) => left.Subtract(right);
        public static bool operator ==(DateTime left, DateTime right) => left.Equals(right);
        public static bool operator !=(DateTime left, DateTime right) => !left.Equals(right);
        public static bool operator <(DateTime left, DateTime right) => left._ticks < right._ticks;
        public static bool operator <=(DateTime left, DateTime right) => left._ticks <= right._ticks;
        public static bool operator >(DateTime left, DateTime right) => left._ticks > right._ticks;
        public static bool operator >=(DateTime left, DateTime right) => left._ticks >= right._ticks;

        private void GetDateParts(out int year, out int month, out int day) =>
            GregorianDateMath.GetDateParts(
                (int)(_ticks / TimeSpan.TicksPerDay), out year, out month, out day);

        private DateTime AddUnits(double value, long maxUnitCount, long ticksPerUnit)
        {
            if (double.IsNaN(value) || Math.Abs(value) > maxUnitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            var integralPart = Math.Truncate(value);
            var fractionalPart = value - integralPart;
            var ticks = checked((long)integralPart * ticksPerUnit);
            ticks = checked(ticks + (long)(fractionalPart * ticksPerUnit));
            return AddTicks(ticks);
        }

        private static long DoubleDateToTicks(double value)
        {
            if (!(value < OADateMaxAsDouble) || !(value > OADateMinAsDouble))
            {
                throw new ArgumentException();
            }

            var milliseconds = (long)(value * MillisecondsPerDay + (value >= 0 ? 0.5 : -0.5));
            if (milliseconds < 0)
            {
                milliseconds -= (milliseconds % MillisecondsPerDay) * 2;
            }

            milliseconds += DoubleDateOffset / TimeSpan.TicksPerMillisecond;
            if (milliseconds < 0 || milliseconds > MaxTicks / TimeSpan.TicksPerMillisecond)
            {
                throw new ArgumentException();
            }
            return milliseconds * TimeSpan.TicksPerMillisecond;
        }

        private static double TicksToOADate(long value)
        {
            if (value == 0)
            {
                return 0.0;
            }
            if (value < TimeSpan.TicksPerDay)
            {
                value += DoubleDateOffset;
            }
            if (value < OADateMinAsTicks)
            {
                throw new OverflowException();
            }

            var milliseconds = (value - DoubleDateOffset) / TimeSpan.TicksPerMillisecond;
            if (milliseconds < 0)
            {
                var fraction = milliseconds % MillisecondsPerDay;
                if (fraction != 0)
                {
                    milliseconds -= (MillisecondsPerDay + fraction) * 2;
                }
            }
            return (double)milliseconds / MillisecondsPerDay;
        }

        private static long TimeToTicks(int hour, int minute, int second)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException();
            }
            return hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond;
        }

        private static long TimeToTicks(
            int hour, int minute, int second, int millisecond, int microsecond)
        {
            if ((uint)millisecond >= 1000 || (uint)microsecond >= 1000)
            {
                throw new ArgumentOutOfRangeException();
            }
            return TimeToTicks(hour, minute, second)
                + millisecond * TimeSpan.TicksPerMillisecond
                + microsecond * TimeSpan.TicksPerMicrosecond;
        }

        private static int ValidateMillisecond(int millisecond)
        {
            if ((uint)millisecond >= 1000)
            {
                throw new ArgumentOutOfRangeException();
            }
            return millisecond;
        }

        private string FormatInvariant(string? format)
        {
            if (format is not null && format.Length != 0 && !IsSupportedFormat(format))
            {
                throw new FormatException();
            }
            return format is "O" or "o" ? FormatRoundTrip() : ToString();
        }

        private string FormatRoundTrip()
        {
            var suffix = _kind switch
            {
                DateTimeKind.Utc => "Z",
                DateTimeKind.Local => FormatOffset(
                    ResolveLocalTimeOffset(_ticks, LocalTimeBasis.Local).Ticks),
                _ => string.Empty,
            };
            return InvariantDateTimeText.FourDigits(Year) + "-"
                + InvariantDateTimeText.TwoDigits(Month) + "-"
                + InvariantDateTimeText.TwoDigits(Day) + "T"
                + InvariantDateTimeText.TwoDigits(Hour) + ":"
                + InvariantDateTimeText.TwoDigits(Minute) + ":"
                + InvariantDateTimeText.TwoDigits(Second) + "."
                + (_ticks % TimeSpan.TicksPerSecond).ToString().PadLeft(7, '0')
                + suffix;
        }

        private static string FormatOffset(long ticks)
        {
            var negative = ticks < 0;
            var minutes = ticks / TimeSpan.TicksPerMinute;
            if (minutes < 0)
            {
                minutes = -minutes;
            }
            return (negative ? "-" : "+")
                + InvariantDateTimeText.TwoDigits((int)(minutes / 60)) + ":"
                + InvariantDateTimeText.TwoDigits((int)(minutes % 60));
        }

        private static bool IsSupportedFormat(string? format) =>
            format is not null && (format.Length == 0 || format is "O" or "o" or "s" or "u" or "G" or "g");

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
            const System.Globalization.DateTimeStyles supported =
                System.Globalization.DateTimeStyles.AllowWhiteSpaces |
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.RoundtripKind;
            if ((style & ~supported) != 0)
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

    public enum DateTimeKind
    {
        Unspecified,
        Utc,
        Local,
    }

    internal static class DateTimeClock
    {
        internal static long UtcNowTicks()
        {
            var timestamp = Runtime.InteropServices.PlatformServices.Clock
                .GetUtcTime();
            if (timestamp.Seconds > long.MaxValue)
            {
                throw new OverflowException();
            }
            return checked(
                GregorianDateMath.UnixEpochTicks
                + (long)timestamp.Seconds * TimeSpan.TicksPerSecond
                + timestamp.Nanoseconds / 100);
        }

    }
}
