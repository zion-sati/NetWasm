// Adapted from dotnet/runtime System.Private.CoreLib DateTime.
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
    public readonly struct DateTime : IComparable, IComparable<DateTime>, IEquatable<DateTime>, IConvertible,
        IFormattable, IParsable<DateTime>, ISpanFormattable, ISpanParsable<DateTime>,
        IUtf8SpanFormattable
    {
        private const ulong TicksMask = 0x3fff_ffff_ffff_ffffUL;
        private const ulong FlagsMask = 0xc000_0000_0000_0000UL;
        internal const ulong KindUtc = 0x4000_0000_0000_0000UL;
        private const ulong KindLocal = 0x8000_0000_0000_0000UL;
        private const ulong KindLocalAmbiguousDst = 0xc000_0000_0000_0000UL;
        private const int KindShift = 62;

        // Keep the upstream four-state representation. The second local state
        // distinguishes the daylight occurrence of an ambiguous local clock value.
        internal readonly ulong _dateData;

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
            _dateData = (ulong)ticks | ((ulong)(uint)kind << KindShift);
        }

        private DateTime(ulong dateData)
        {
            _dateData = dateData;
        }

        internal DateTime(long ticks, DateTimeKind kind, bool isAmbiguousDst)
        {
            if ((ulong)ticks > (ulong)MaxTicks)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (kind != DateTimeKind.Local)
            {
                throw new ArgumentException();
            }
            _dateData = (ulong)ticks |
                (isAmbiguousDst ? KindLocalAmbiguousDst : KindLocal);
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

        // The Calendar constructors are ported from dotnet/runtime commit
        // deae2e32bf0e018e09d64597b7c59b62b73bb3ce. NetWasm's clock has no leap seconds, so
        // upstream's leap-second branch never applies.
        public DateTime(int year, int month, int day, Globalization.Calendar calendar)
            : this(year, month, day, 0, 0, 0, calendar)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, Globalization.Calendar calendar)
        {
            ArgumentNullException.ThrowIfNull(calendar);

            _dateData = (ulong)calendar.ToDateTime(year, month, day, hour, minute, second, 0).Ticks;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, Globalization.Calendar calendar)
        {
            ArgumentNullException.ThrowIfNull(calendar);

            _dateData = (ulong)calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).Ticks;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, Globalization.Calendar calendar, DateTimeKind kind)
        {
            ArgumentNullException.ThrowIfNull(calendar);

            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecond));
            }
            if ((uint)kind > (uint)DateTimeKind.Local)
            {
                throw new ArgumentException();
            }

            ulong ticks = (ulong)calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).Ticks;
            _dateData = ticks | ((ulong)(uint)kind << KindShift);
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
        internal const long MinTicks = 0;
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

        public bool IsDaylightSavingTime() => Kind != DateTimeKind.Utc &&
            ResolveLocalTimeOffset(
                Ticks,
                IsAmbiguousDaylightSavingTime()
                    ? LocalTimeBasis.LocalAmbiguousDaylight
                    : LocalTimeBasis.Local)
                .IsDaylightSavingTime;

        public DateTime ToLocalTime()
        {
            if (Kind == DateTimeKind.Local)
            {
                return this;
            }
            var offset = ResolveLocalTimeOffset(Ticks, LocalTimeBasis.Utc);
            var localTicks = Ticks + offset.Ticks;
            if ((ulong)localTicks > (ulong)MaxTicks)
            {
                localTicks = localTicks < 0 ? 0 : MaxTicks;
            }
            return new DateTime(
                localTicks,
                DateTimeKind.Local,
                offset.IsAmbiguousDaylightSavingTime);
        }

        public DateTime ToUniversalTime()
        {
            if (Kind == DateTimeKind.Utc)
            {
                return this;
            }
            var offset = ResolveLocalTimeOffset(
                Ticks,
                IsAmbiguousDaylightSavingTime()
                    ? LocalTimeBasis.LocalAmbiguousDaylight
                    : LocalTimeBasis.Local);
            var utcTicks = Ticks - offset.Ticks;
            if ((ulong)utcTicks > (ulong)MaxTicks)
            {
                utcTicks = utcTicks < 0 ? 0 : MaxTicks;
            }
            return new DateTime(utcTicks, DateTimeKind.Utc);
        }

        private static LocalTimeOffset ResolveLocalTimeOffset(
            long ticks,
            LocalTimeBasis basis)
        {
            PlatformServices.EnsureLocalTimeReady();
            return PlatformServices.LocalTimeOffsets.Resolve(ticks, basis);
        }
        private ulong InternalKind => _dateData & FlagsMask;
        internal bool IsAmbiguousDaylightSavingTime() =>
            _dateData >= KindLocalAmbiguousDst;
        public long Ticks { get { return (long)(_dateData & TicksMask); } }
        public DateTimeKind Kind => InternalKind switch
        {
            0 => DateTimeKind.Unspecified,
            KindUtc => DateTimeKind.Utc,
            _ => DateTimeKind.Local,
        };
        public DateTime Date => new((ulong)(Ticks - Ticks % TimeSpan.TicksPerDay) | InternalKind);
        public TimeSpan TimeOfDay { get { return new(Ticks % TimeSpan.TicksPerDay); } }
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
        public int Hour { get { return (int)(Ticks / TimeSpan.TicksPerHour % 24); } }
        public int Minute { get { return (int)(Ticks / TimeSpan.TicksPerMinute % 60); } }
        public int Second { get { return (int)(Ticks / TimeSpan.TicksPerSecond % 60); } }
        public int Millisecond { get { return (int)(Ticks / TimeSpan.TicksPerMillisecond % 1000); } }
        public int Microsecond { get { return (int)(Ticks / TimeSpan.TicksPerMicrosecond % 1000); } }
        public int Nanosecond { get { return (int)(Ticks % TimeSpan.TicksPerMicrosecond) * 100; } }
        public DayOfWeek DayOfWeek { get { return (DayOfWeek)((Ticks / TimeSpan.TicksPerDay + 1) % 7); } }

        public DateTime Add(TimeSpan value) => AddTicks(value.Ticks);
        public DateTime AddTicks(long value)
        {
            var ticks = unchecked(Ticks + value);
            if ((ulong)ticks > (ulong)MaxTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new DateTime((ulong)ticks | InternalKind);
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
            var ticks = checked(
                (long)date.DayNumber * TimeSpan.TicksPerDay + TimeOfDay.Ticks);
            return new DateTime((ulong)ticks | InternalKind);
        }
        public DateTime AddYears(int value)
        {
            if (value < -10000 || value > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return AddMonths(value * 12);
        }
        public TimeSpan Subtract(DateTime value) => new(Ticks - value.Ticks);
        public DateTime Subtract(TimeSpan value)
        {
            var ticks = (ulong)(Ticks - value.Ticks);
            if (ticks > (ulong)MaxTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new DateTime(ticks | InternalKind);
        }
        public static bool IsLeapYear(int year) => GregorianDateMath.IsLeapYear(year);
        public static int DaysInMonth(int year, int month) => DateOnly.DaysInMonth(year, month);

        public static DateTime FromOADate(double value) =>
            new(DoubleDateToTicks(value), DateTimeKind.Unspecified);

        public double ToOADate() => TicksToOADate(Ticks);

        public long ToBinary()
        {
            if (Kind == DateTimeKind.Local)
            {
                throw new PlatformNotSupportedException(
                    "Local time-zone conversion is unavailable in the invariant profile.");
            }
            return (long)_dateData;
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
            var ticks = Kind == DateTimeKind.Local ? ToUniversalTime().Ticks : Ticks;
            var fileTime = ticks - FileTimeOffsetTicks;
            if (fileTime < 0) throw new ArgumentOutOfRangeException();
            return fileTime;
        }

        public static DateTime FromFileTimeUtc(long fileTime)
        {
            if ((ulong)fileTime > (ulong)(MaxTicks - FileTimeOffsetTicks))
            {
                throw new ArgumentOutOfRangeException(nameof(fileTime));
            }
            return new DateTime(fileTime + FileTimeOffsetTicks, DateTimeKind.Utc);
        }

        public long ToFileTime() => ToUniversalTime().ToFileTimeUtc();

        public static DateTime FromFileTime(long fileTime) =>
            FromFileTimeUtc(fileTime).ToLocalTime();

        public TypeCode GetTypeCode() => TypeCode.DateTime;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => throw InvalidConversion(nameof(Boolean));
        char IConvertible.ToChar(IFormatProvider? provider) => throw InvalidConversion(nameof(Char));
        sbyte IConvertible.ToSByte(IFormatProvider? provider) => throw InvalidConversion(nameof(SByte));
        byte IConvertible.ToByte(IFormatProvider? provider) => throw InvalidConversion(nameof(Byte));
        short IConvertible.ToInt16(IFormatProvider? provider) => throw InvalidConversion(nameof(Int16));
        ushort IConvertible.ToUInt16(IFormatProvider? provider) => throw InvalidConversion(nameof(UInt16));
        int IConvertible.ToInt32(IFormatProvider? provider) => throw InvalidConversion(nameof(Int32));
        uint IConvertible.ToUInt32(IFormatProvider? provider) => throw InvalidConversion(nameof(UInt32));
        long IConvertible.ToInt64(IFormatProvider? provider) => throw InvalidConversion(nameof(Int64));
        ulong IConvertible.ToUInt64(IFormatProvider? provider) => throw InvalidConversion(nameof(UInt64));
        float IConvertible.ToSingle(IFormatProvider? provider) => throw InvalidConversion(nameof(Single));
        double IConvertible.ToDouble(IFormatProvider? provider) => throw InvalidConversion(nameof(Double));
        decimal IConvertible.ToDecimal(IFormatProvider? provider) => throw InvalidConversion(nameof(Decimal));

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) => this;

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(conversionType);
            // DateTime's successful DefaultToType cases are identity, Object
            // and String. Every numeric, Boolean and Char conversion is invalid.
            if (conversionType == typeof(DateTime) || conversionType == typeof(object)) return this;
            if (conversionType == typeof(string)) return ToString(provider);
            throw InvalidConversion(conversionType.FullName ?? conversionType.Name);
        }

        private static InvalidCastException InvalidConversion(string target) =>
            new InvalidCastException("Invalid cast from 'DateTime' to '" + target + "'.");

        public int CompareTo(DateTime value) => Ticks.CompareTo(value.Ticks);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is DateTime other
                ? CompareTo(other)
                : throw new ArgumentException();
        public static int Compare(DateTime left, DateTime right) =>
            left.Ticks.CompareTo(right.Ticks);
        public bool Equals(DateTime value) => Ticks == value.Ticks;
        public override bool Equals(object? value) => value is DateTime other && Equals(other);
        public override int GetHashCode() => Ticks.GetHashCode();

        public static bool Equals(DateTime left, DateTime right) => left.Equals(right);

        public void Deconstruct(out int year, out int month, out int day) =>
            GetDateParts(out year, out month, out day);

        public void Deconstruct(out DateOnly date, out TimeOnly time)
        {
            date = DateOnly.FromDateTime(this);
            time = TimeOnly.FromDateTime(this);
        }

        public static DateTime SpecifyKind(DateTime value, DateTimeKind kind) =>
            new(value.Ticks, kind);
        // Text members ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
        // Constructs a DateTime from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTime Parse(string s)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return DateTimeParse.Parse(s, DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None);
        }

        // Constructs a DateTime from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTime Parse(string s, IFormatProvider? provider)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return DateTimeParse.Parse(s, DateTimeFormatInfo.GetInstance(provider), DateTimeStyles.None);
        }

        public static DateTime Parse(string s, IFormatProvider? provider, DateTimeStyles styles)
        {
            DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return DateTimeParse.Parse(s, DateTimeFormatInfo.GetInstance(provider), styles);
        }

        public static DateTime Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null, DateTimeStyles styles = DateTimeStyles.None)
        {
            DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            return DateTimeParse.Parse(s, DateTimeFormatInfo.GetInstance(provider), styles);
        }

        // Constructs a DateTime from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTime ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format, IFormatProvider? provider)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            if (format == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.format);
            return DateTimeParse.ParseExact(s, format, DateTimeFormatInfo.GetInstance(provider), DateTimeStyles.None);
        }

        // Constructs a DateTime from a string. The string must specify a
        // date and optionally a time in a culture-specific or universal format.
        // Leading and trailing whitespace characters are allowed.
        //
        public static DateTime ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format, IFormatProvider? provider, DateTimeStyles style)
        {
            DateTimeFormatInfo.ValidateStyles(style);
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            if (format == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.format);
            return DateTimeParse.ParseExact(s, format, DateTimeFormatInfo.GetInstance(provider), style);
        }

        public static DateTime ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            DateTimeFormatInfo.ValidateStyles(style);
            return DateTimeParse.ParseExact(s, format, DateTimeFormatInfo.GetInstance(provider), style);
        }

        public static DateTime ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string[] formats, IFormatProvider? provider, DateTimeStyles style)
        {
            DateTimeFormatInfo.ValidateStyles(style);
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return DateTimeParse.ParseExactMultiple(s, formats, DateTimeFormatInfo.GetInstance(provider), style);
        }

        public static DateTime ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string[] formats, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            DateTimeFormatInfo.ValidateStyles(style);
            return DateTimeParse.ParseExactMultiple(s, formats, DateTimeFormatInfo.GetInstance(provider), style);
        }

        public string ToLongDateString()
        {
            return DateTimeFormat.Format(this, "D", null);
        }

        public string ToLongTimeString()
        {
            return DateTimeFormat.Format(this, "T", null);
        }

        public string ToShortDateString()
        {
            return DateTimeFormat.Format(this, "d", null);
        }

        public string ToShortTimeString()
        {
            return DateTimeFormat.Format(this, "t", null);
        }

        public override string ToString()
        {
            // NetWasm: see InvariantDateTimeDefaults.
            return DateTimeFormatInfo.s_currentInfo is null
                ? InvariantDateTimeDefaults.General(this)
                : DateTimeFormat.Format(this, null, null);
        }

        public string ToString([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string? format)
        {
            return DateTimeFormat.Format(this, format, null);
        }

        public string ToString(IFormatProvider? provider)
        {
            return DateTimeFormat.Format(this, null, provider);
        }

        public string ToString([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string? format, IFormatProvider? provider)
        {
            return DateTimeFormat.Format(this, format, provider);
        }

        public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            DateTimeFormat.TryFormat(this, destination, out charsWritten, format, provider);

        /// <inheritdoc cref="IUtf8SpanFormattable.TryFormat" />
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            DateTimeFormat.TryFormat(this, utf8Destination, out bytesWritten, format, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, out DateTime result)
        {
            if (s == null)
            {
                result = default;
                return false;
            }
            return DateTimeParse.TryParse(s, DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out DateTime result)
        {
            return DateTimeParse.TryParse(s, DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None, out result);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, DateTimeStyles styles, out DateTime result)
        {
            DateTimeFormatInfo.ValidateStyles(styles, styles: true);

            if (s == null)
            {
                result = default;
                return false;
            }

            return DateTimeParse.TryParse(s, DateTimeFormatInfo.GetInstance(provider), styles, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, DateTimeStyles styles, out DateTime result)
        {
            DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            return DateTimeParse.TryParse(s, DateTimeFormatInfo.GetInstance(provider), styles, out result);
        }

        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string? format, IFormatProvider? provider, DateTimeStyles style, out DateTime result)
        {
            DateTimeFormatInfo.ValidateStyles(style);

            if (s == null || format == null)
            {
                result = default;
                return false;
            }

            return DateTimeParse.TryParseExact(s, format, DateTimeFormatInfo.GetInstance(provider), style, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] ReadOnlySpan<char> format, IFormatProvider? provider, DateTimeStyles style, out DateTime result)
        {
            DateTimeFormatInfo.ValidateStyles(style);
            return DateTimeParse.TryParseExact(s, format, DateTimeFormatInfo.GetInstance(provider), style, out result);
        }

        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out DateTime result)
        {
            DateTimeFormatInfo.ValidateStyles(style);

            if (s == null)
            {
                result = default;
                return false;
            }

            return DateTimeParse.TryParseExactMultiple(s, formats, DateTimeFormatInfo.GetInstance(provider), style, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out DateTime result)
        {
            DateTimeFormatInfo.ValidateStyles(style);
            return DateTimeParse.TryParseExactMultiple(s, formats, DateTimeFormatInfo.GetInstance(provider), style, out result);
        }


        // Returns a string array containing all of the known date and time options for the
        // current culture.  The strings returned are properly formatted date and
        // time strings for the current instance of DateTime.
        public string[] GetDateTimeFormats()
        {
            return GetDateTimeFormats(CultureInfo.CurrentCulture);
        }

        // Returns a string array containing all of the known date and time options for the
        // using the information provided by IFormatProvider.  The strings returned are properly formatted date and
        // time strings for the current instance of DateTime.
        public string[] GetDateTimeFormats(IFormatProvider? provider)
        {
            return DateTimeFormat.GetAllDateTimes(this, DateTimeFormatInfo.GetInstance(provider));
        }

        // Returns a string array containing all of the date and time options for the
        // given format format and current culture.  The strings returned are properly formatted date and
        // time strings for the current instance of DateTime.
        public string[] GetDateTimeFormats(char format)
        {
            return GetDateTimeFormats(format, CultureInfo.CurrentCulture);
        }

        // Returns a string array containing all of the date and time options for the
        // given format format and given culture.  The strings returned are properly formatted date and
        // time strings for the current instance of DateTime.
        public string[] GetDateTimeFormats(char format, IFormatProvider? provider)
        {
            return DateTimeFormat.GetAllDateTimes(this, format, DateTimeFormatInfo.GetInstance(provider));
        }

        //
        // IParsable
        //

        /// <inheritdoc cref="IParsable{TSelf}.TryParse(string?, IFormatProvider?, out TSelf)" />
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out DateTime result) => TryParse(s, provider, DateTimeStyles.None, out result);

        //
        // ISpanParsable
        //

        /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)" />
        public static DateTime Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, provider, DateTimeStyles.None);

        /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse(ReadOnlySpan{char}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTime result) => TryParse(s, provider, DateTimeStyles.None, out result);

        public static DateTime operator +(DateTime value, TimeSpan span) => value.Add(span);
        public static DateTime operator -(DateTime value, TimeSpan span) => value.Subtract(span);
        public static TimeSpan operator -(DateTime left, DateTime right) => left.Subtract(right);
        public static bool operator ==(DateTime left, DateTime right) => left.Equals(right);
        public static bool operator !=(DateTime left, DateTime right) => !left.Equals(right);
        public static bool operator <(DateTime left, DateTime right) => left.Ticks < right.Ticks;
        public static bool operator <=(DateTime left, DateTime right) => left.Ticks <= right.Ticks;
        public static bool operator >(DateTime left, DateTime right) => left.Ticks > right.Ticks;
        public static bool operator >=(DateTime left, DateTime right) => left.Ticks >= right.Ticks;

        // Upstream's internal helpers that DateTimeFormat and DateTimeParse call, over NetWasm's date arithmetic.
        // NetWasm's clock has no leap seconds, so a second of 60 never makes a valid time.

        // TryAddTicks is exact as AddTicks except it doesn't throw
        internal bool TryAddTicks(long value, out DateTime result)
        {
            ulong ticks = (ulong)(Ticks + value);
            if (ticks > MaxTicks)
            {
                result = default;
                return false;
            }
            result = new DateTime(ticks | InternalKind);
            return true;
        }

        internal void GetTime(out int hour, out int minute, out int second)
        {
            ulong seconds = (ulong)Ticks / TimeSpan.TicksPerSecond;
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        internal void GetTimePrecise(out int hour, out int minute, out int second, out int tick)
        {
            ulong ticks = (ulong)Ticks;
            ulong seconds = ticks / TimeSpan.TicksPerSecond;
            tick = (int)(ticks - (seconds * TimeSpan.TicksPerSecond));
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        // Tries to construct a DateTime from a given year, month, day, hour,
        // minute, second and millisecond.
        //
        internal static bool TryCreate(int year, int month, int day, int hour, int minute, int second, int millisecond, out DateTime result)
        {
            result = default;
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1)
            {
                return false;
            }

            // Per the ISO 8601 standard, 24:00:00 represents end of a calendar day
            // (same instant as next day's 00:00:00), but only when minute, second, and millisecond are all zero.
            // We treat it as hour=0 and add one day at the end.
            bool isEndOfDay = false;
            if (hour == 24)
            {
                if (minute != 0 || second != 0 || millisecond != 0)
                {
                    return false;
                }

                hour = 0;
                isEndOfDay = true;
            }

            if ((uint)hour > 24 || (uint)minute >= 60 || (uint)second >= 60 || (uint)millisecond >= TimeSpan.MillisecondsPerSecond)
            {
                return false;
            }

            if (day > DaysInMonth(year, month))
            {
                return false;
            }
            long ticks = GregorianDateMath.DateToDayNumber(year, month, day) * TimeSpan.TicksPerDay
                + TimeToTicks(hour, minute, second) + millisecond * TimeSpan.TicksPerMillisecond;

            // If hour was originally 24 (end of day per ISO 8601), add one day to advance to next day's 00:00:00
            if (isEndOfDay)
            {
                ticks += TimeSpan.TicksPerDay;
                if (ticks > MaxTicks)
                {
                    return false;
                }
            }

            result = new DateTime(ticks);
            return true;
        }

        private void GetDateParts(out int year, out int month, out int day) =>
            GregorianDateMath.GetDateParts(
                (int)(Ticks / TimeSpan.TicksPerDay), out year, out month, out day);

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

    }

    public enum DateTimeKind
    {
        Unspecified,
        Utc,
        Local,
    }

    internal static class DateTimeClock
    {
        internal static long UtcNowTicks() =>
            Runtime.InteropServices.PlatformServices.Clock.GetUtcNow().Ticks;

    }
}
