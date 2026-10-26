// Adapted from dotnet/runtime System.Private.CoreLib DateTime.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using Runtime.InteropServices;
    using Runtime.InteropServices.TimeZones;

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
        public override string ToString() => FormatGeneral(includeSeconds: true);

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
            if (TryParseGeneralText(value, out result))
            {
                return true;
            }
            return TryParseIsoText(value, out result);
        }

        private static bool TryParseIsoText(string? value, out DateTime result)
        {
            result = default;
            if (value is null || value.Length < 19 ||
                !InvariantDateTimeText.TryParseIso(
                    value.Substring(0, 19),
                    'T',
                    includeSeconds: true,
                    out var clock))
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
            result = new DateTime(clock.Ticks + fraction, kind);
            return true;
        }

        private static bool TryParseGeneralText(string? value, out DateTime result)
        {
            result = default;
            if (value is null)
            {
                return false;
            }
            if (InvariantDateTimeText.TryParseGeneral(
                value,
                includeSeconds: true,
                out result))
            {
                return true;
            }
            return InvariantDateTimeText.TryParseGeneral(
                value,
                includeSeconds: false,
                out result);
        }

        public static bool TryParse(string? value, IFormatProvider? provider, out DateTime result) =>
            TryParse(value, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(string? value, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateTime result)
        {
            ValidateStyles(style);
            value = TrimWhiteSpace(value, style);
            if (value is not null &&
                (style & System.Globalization.DateTimeStyles.AllowInnerWhite) != 0)
            {
                value = CollapseInnerWhite(value);
            }
            if (!TryParse(value, out result)) return false;
            return ApplyStyles(ref result, style);
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
            if (value is null || !IsSupportedFormat(format)) return false;
            var exactValue = TrimWhiteSpace(value, style)!;
            if ((style & System.Globalization.DateTimeStyles.AllowInnerWhite) != 0)
            {
                exactValue = CollapseInnerWhite(exactValue);
            }
            var parsed = format switch
            {
                "s" => InvariantDateTimeText.TryParseIso(
                    exactValue,
                    'T',
                    includeSeconds: true,
                    out result),
                "u" => exactValue.Length == 20 && exactValue[19] == 'Z' &&
                    InvariantDateTimeText.TryParseIso(
                        exactValue.Substring(0, 19),
                        ' ',
                        includeSeconds: true,
                        out result),
                "G" => InvariantDateTimeText.TryParseGeneral(
                    exactValue,
                    includeSeconds: true,
                    out result),
                "g" => InvariantDateTimeText.TryParseGeneral(
                    exactValue,
                    includeSeconds: false,
                    out result),
                "O" or "o" => TryParseRoundTrip(exactValue, out result),
                _ when format == InvariantDateTimeText.Rfc3339Format =>
                    TryParse(exactValue, out result),
                _ => TryParseCustomExact(exactValue, format!, out result),
            };
            if (!parsed)
            {
                return false;
            }
            return ApplyStyles(ref result, style);
        }

        private static bool TryParseCustomExact(
            string value,
            string format,
            out DateTime result)
        {
            result = default;
            if (!InvariantDateTimePattern.TryParse(
                value,
                format,
                out var clock,
                out var hasOffset,
                out var offset,
                out var utcDesignator))
            {
                return false;
            }
            if (utcDesignator)
            {
                result = new DateTime(clock.Ticks, DateTimeKind.Utc);
                return true;
            }
            if (!hasOffset)
            {
                result = clock;
                return true;
            }
            try
            {
                result = new DateTimeOffset(clock, offset).LocalDateTime;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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
        public static bool operator <(DateTime left, DateTime right) => left.Ticks < right.Ticks;
        public static bool operator <=(DateTime left, DateTime right) => left.Ticks <= right.Ticks;
        public static bool operator >(DateTime left, DateTime right) => left.Ticks > right.Ticks;
        public static bool operator >=(DateTime left, DateTime right) => left.Ticks >= right.Ticks;

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

        private string FormatInvariant(string? format)
        {
            if (format is { Length: 1 } && !IsSupportedFormat(format))
            {
                throw new FormatException();
            }
            if (format == InvariantDateTimeText.Rfc3339Format)
            {
                var suffix = Kind switch
                {
                    DateTimeKind.Utc => "Z",
                    DateTimeKind.Local => FormatOffset(
                        ResolveLocalTimeOffset(
                            Ticks,
                            IsAmbiguousDaylightSavingTime()
                                ? LocalTimeBasis.LocalAmbiguousDaylight
                                : LocalTimeBasis.Local).Ticks),
                    _ => string.Empty,
                };
                return InvariantDateTimeText.FormatRfc3339(this, suffix);
            }
            if (format is { Length: > 1 })
            {
                var offset = Kind == DateTimeKind.Utc
                    ? TimeSpan.Zero
                    : new TimeSpan(ResolveLocalTimeOffset(
                        Ticks,
                        IsAmbiguousDaylightSavingTime()
                            ? LocalTimeBasis.LocalAmbiguousDaylight
                            : LocalTimeBasis.Local).Ticks);
                return InvariantDateTimePattern.Format(
                    this,
                    format,
                    hasOffset: true,
                    offset,
                    explicitOffset: false);
            }
            return format switch
            {
                "O" or "o" => FormatRoundTrip(),
                "s" => FormatSortable(),
                "u" => FormatUniversalSortable(),
                "g" => FormatGeneral(includeSeconds: false),
                _ => FormatGeneral(includeSeconds: true),
            };
        }

        internal string FormatGeneral(bool includeSeconds) =>
            InvariantDateTimeText.TwoDigits(Month) + "/" +
            InvariantDateTimeText.TwoDigits(Day) + "/" +
            InvariantDateTimeText.FourDigits(Year) + " " +
            InvariantDateTimeText.TwoDigits(Hour) + ":" +
            InvariantDateTimeText.TwoDigits(Minute) +
            (includeSeconds
                ? ":" + InvariantDateTimeText.TwoDigits(Second)
                : string.Empty);

        private string FormatSortable() =>
            InvariantDateTimeText.FourDigits(Year) + "-" +
            InvariantDateTimeText.TwoDigits(Month) + "-" +
            InvariantDateTimeText.TwoDigits(Day) + "T" +
            InvariantDateTimeText.TwoDigits(Hour) + ":" +
            InvariantDateTimeText.TwoDigits(Minute) + ":" +
            InvariantDateTimeText.TwoDigits(Second);

        private string FormatUniversalSortable() =>
            InvariantDateTimeText.FourDigits(Year) + "-" +
            InvariantDateTimeText.TwoDigits(Month) + "-" +
            InvariantDateTimeText.TwoDigits(Day) + " " +
            InvariantDateTimeText.TwoDigits(Hour) + ":" +
            InvariantDateTimeText.TwoDigits(Minute) + ":" +
            InvariantDateTimeText.TwoDigits(Second) + "Z";

        private string FormatRoundTrip()
        {
            var suffix = Kind switch
            {
                DateTimeKind.Utc => "Z",
                DateTimeKind.Local => FormatOffset(
                    ResolveLocalTimeOffset(
                        Ticks,
                        IsAmbiguousDaylightSavingTime()
                            ? LocalTimeBasis.LocalAmbiguousDaylight
                            : LocalTimeBasis.Local).Ticks),
                _ => string.Empty,
            };
            return InvariantDateTimeText.FourDigits(Year) + "-"
                + InvariantDateTimeText.TwoDigits(Month) + "-"
                + InvariantDateTimeText.TwoDigits(Day) + "T"
                + InvariantDateTimeText.TwoDigits(Hour) + ":"
                + InvariantDateTimeText.TwoDigits(Minute) + ":"
                + InvariantDateTimeText.TwoDigits(Second) + "."
                + (Ticks % TimeSpan.TicksPerSecond).ToString().PadLeft(7, '0')
                + suffix;
        }

        private static bool TryParseRoundTrip(string value, out DateTime result)
        {
            result = default;
            if (value.Length is not (27 or 28 or 33) ||
                !InvariantDateTimeText.HasSeparator(value, 19, '.') ||
                !InvariantDateTimeText.TryReadFraction(value, 20, 27, out var fraction) ||
                !InvariantDateTimeText.TryParseIso(
                    value.Substring(0, 19),
                    'T',
                    includeSeconds: true,
                    out var clock))
            {
                return false;
            }
            clock = clock.AddTicks(fraction);
            if (value.Length == 27)
            {
                result = clock;
                return true;
            }
            if (value.Length == 28 && value[27] == 'Z')
            {
                result = new DateTime(clock.Ticks, DateTimeKind.Utc);
                return true;
            }
            if (value.Length != 33 || value[27] is not ('+' or '-') ||
                !InvariantDateTimeText.TryReadDigits(value, 28, 2, out var hours) ||
                !InvariantDateTimeText.HasSeparator(value, 30, ':') ||
                !InvariantDateTimeText.TryReadDigits(value, 31, 2, out var minutes) ||
                hours > 14 || minutes >= 60 || hours == 14 && minutes != 0)
            {
                return false;
            }
            var offset = TimeSpan.FromMinutes(hours * 60 + minutes);
            if (value[27] == '-')
            {
                offset = -offset;
            }
            try
            {
                result = new DateTimeOffset(clock, offset).LocalDateTime;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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
            format is not null && (format.Length == 0 || format.Length > 1 ||
                format is "O" or "o" or "s" or "u" or "G" or "g"
                || format == InvariantDateTimeText.Rfc3339Format);

        private static string? TrimWhiteSpace(string? value, System.Globalization.DateTimeStyles style)
        {
            if (value is null)
            {
                return value;
            }
            var start = 0;
            var end = value.Length;
            if ((style & System.Globalization.DateTimeStyles.AllowLeadingWhite) != 0)
            {
                while (start < end && IsWhiteSpace(value[start])) start++;
            }
            if ((style & System.Globalization.DateTimeStyles.AllowTrailingWhite) != 0)
            {
                while (end > start && IsWhiteSpace(value[end - 1])) end--;
            }
            return start == 0 && end == value.Length ? value : value.Substring(start, end - start);
        }

        private static string CollapseInnerWhite(string value)
        {
            var output = string.Empty;
            var inWhiteSpace = false;
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (IsWhiteSpace(current))
                {
                    if (!inWhiteSpace)
                    {
                        output += ' ';
                        inWhiteSpace = true;
                    }
                }
                else
                {
                    output += current;
                    inWhiteSpace = false;
                }
            }
            return output;
        }

        private static bool ApplyStyles(
            ref DateTime result,
            System.Globalization.DateTimeStyles style)
        {
            var adjust =
                (style & System.Globalization.DateTimeStyles.AdjustToUniversal) != 0;
            if (result.Kind == DateTimeKind.Utc)
            {
                return true;
            }
            if (result.Kind == DateTimeKind.Local)
            {
                if (adjust)
                {
                    result = result.ToUniversalTime();
                }
                return true;
            }

            if ((style & System.Globalization.DateTimeStyles.AssumeUniversal) != 0)
            {
                result = new DateTime(result.Ticks, DateTimeKind.Utc);
                if (!adjust)
                {
                    result = result.ToLocalTime();
                }
                return true;
            }
            if ((style & System.Globalization.DateTimeStyles.AssumeLocal) != 0 || adjust)
            {
                result = new DateTime(result.Ticks, DateTimeKind.Local);
                if (adjust)
                {
                    result = result.ToUniversalTime();
                }
            }
            return true;
        }

        private static void ValidateStyles(System.Globalization.DateTimeStyles style)
        {
            const System.Globalization.DateTimeStyles supported =
                System.Globalization.DateTimeStyles.AllowWhiteSpaces |
                System.Globalization.DateTimeStyles.AssumeLocal |
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.RoundtripKind;
            const System.Globalization.DateTimeStyles localUniversal =
                System.Globalization.DateTimeStyles.AssumeLocal |
                System.Globalization.DateTimeStyles.AssumeUniversal;
            var roundtripConflict = System.Globalization.DateTimeStyles.RoundtripKind |
                localUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal;
            if ((style & ~supported) != 0 ||
                (style & localUniversal) == localUniversal ||
                (style & roundtripConflict) >
                    System.Globalization.DateTimeStyles.RoundtripKind)
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
