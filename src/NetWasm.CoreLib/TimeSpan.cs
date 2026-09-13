// Adapted from dotnet/runtime System.Private.CoreLib TimeSpan.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public readonly struct TimeSpan : IComparable, IComparable<TimeSpan>, IEquatable<TimeSpan>,
        IFormattable, IParsable<TimeSpan>, ISpanFormattable, ISpanParsable<TimeSpan>,
        IUtf8SpanFormattable
    {
        public const long NanosecondsPerTick = (long)100;
        public const long TicksPerMicrosecond = (long)10;
        public const long TicksPerMillisecond = (long)10000;
        public const long TicksPerSecond = (long)10000000;
        public const long TicksPerMinute = (long)600000000;
        public const long TicksPerHour = (long)36000000000;
        public const long TicksPerDay = (long)864000000000;
        public const long MicrosecondsPerMillisecond = (long)1000;
        public const long MicrosecondsPerSecond = (long)1000000;
        public const long MicrosecondsPerMinute = (long)60000000;
        public const long MicrosecondsPerHour = (long)3600000000;
        public const long MicrosecondsPerDay = (long)86400000000;
        public const long MillisecondsPerSecond = (long)1000;
        public const long MillisecondsPerMinute = (long)60000;
        public const long MillisecondsPerHour = (long)3600000;
        public const long MillisecondsPerDay = (long)86400000;
        public const long SecondsPerMinute = (long)60;
        public const long SecondsPerHour = (long)3600;
        public const long SecondsPerDay = (long)86400;
        public const long MinutesPerHour = (long)60;
        public const long MinutesPerDay = (long)1440;
        public const int HoursPerDay = 24;

        internal const long MinTicks = long.MinValue;
        internal const long MaxTicks = long.MaxValue;
        internal const long MinMicroseconds = MinTicks / TicksPerMicrosecond;
        internal const long MaxMicroseconds = MaxTicks / TicksPerMicrosecond;
        internal const long MinMilliseconds = MinTicks / TicksPerMillisecond;
        internal const long MaxMilliseconds = MaxTicks / TicksPerMillisecond;
        internal const long MinSeconds = MinTicks / TicksPerSecond;
        internal const long MaxSeconds = MaxTicks / TicksPerSecond;
        internal const long MinMinutes = MinTicks / TicksPerMinute;
        internal const long MaxMinutes = MaxTicks / TicksPerMinute;
        internal const long MinHours = MinTicks / TicksPerHour;
        internal const long MaxHours = MaxTicks / TicksPerHour;
        internal const long MinDays = MinTicks / TicksPerDay;
        internal const long MaxDays = MaxTicks / TicksPerDay;

        public static readonly TimeSpan Zero = new(0);
        public static readonly TimeSpan MaxValue = new(MaxTicks);
        public static readonly TimeSpan MinValue = new(MinTicks);

        // Kept as a single tick field to preserve the upstream value-type layout.
        internal readonly long _ticks;

        public TimeSpan(long ticks) => _ticks = ticks;

        public TimeSpan(int hours, int minutes, int seconds) =>
            _ticks = TimeToTicks(hours, minutes, seconds);

        public TimeSpan(int days, int hours, int minutes, int seconds) :
            this(days, hours, minutes, seconds, 0)
        {
        }

        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds) :
            this(days, hours, minutes, seconds, milliseconds, 0)
        {
        }

        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds, int microseconds)
        {
            var totalMicroseconds = (Int128)days * MicrosecondsPerDay
                + (Int128)hours * MicrosecondsPerHour
                + (Int128)minutes * MicrosecondsPerMinute
                + (Int128)seconds * MicrosecondsPerSecond
                + (Int128)milliseconds * MicrosecondsPerMillisecond
                + microseconds;
            if (totalMicroseconds > (Int128)MaxMicroseconds ||
                totalMicroseconds < (Int128)MinMicroseconds)
            {
                throw new ArgumentOutOfRangeException();
            }
            _ticks = (long)totalMicroseconds * TicksPerMicrosecond;
        }

        public long Ticks { get { return _ticks; } }
        public int Days { get { return (int)(_ticks / TicksPerDay); } }
        public int Hours { get { return (int)(_ticks / TicksPerHour % HoursPerDay); } }
        public int Milliseconds { get { return (int)(_ticks / TicksPerMillisecond % MillisecondsPerSecond); } }
        public int Microseconds { get { return (int)(_ticks / TicksPerMicrosecond % MicrosecondsPerMillisecond); } }
        public int Nanoseconds { get { return (int)(_ticks % TicksPerMicrosecond * NanosecondsPerTick); } }
        public int Minutes { get { return (int)(_ticks / TicksPerMinute % MinutesPerHour); } }
        public int Seconds { get { return (int)(_ticks / TicksPerSecond % SecondsPerMinute); } }
        public double TotalDays { get { return (double)_ticks / TicksPerDay; } }
        public double TotalHours { get { return (double)_ticks / TicksPerHour; } }
        public double TotalMilliseconds
        {
            get
            {
                var value = (double)_ticks / TicksPerMillisecond;
                return value > MaxMilliseconds ? MaxMilliseconds
                    : value < MinMilliseconds ? MinMilliseconds
                    : value;
            }
        }
        public double TotalMicroseconds { get { return (double)_ticks / TicksPerMicrosecond; } }
        public double TotalNanoseconds { get { return (double)_ticks * NanosecondsPerTick; } }
        public double TotalMinutes { get { return (double)_ticks / TicksPerMinute; } }
        public double TotalSeconds { get { return (double)_ticks / TicksPerSecond; } }

        public TimeSpan Add(TimeSpan value) => this + value;
        public TimeSpan Subtract(TimeSpan value) => this - value;

        public static int Compare(TimeSpan left, TimeSpan right) => left._ticks.CompareTo(right._ticks);

        public int CompareTo(object? value)
        {
            if (value is null) return 1;
            return value is TimeSpan other
                ? CompareTo(other)
                : throw new ArgumentException();
        }

        public int CompareTo(TimeSpan value) => Compare(this, value);

        public TimeSpan Duration() => _ticks == MinTicks
            ? throw new OverflowException()
            : new(_ticks < 0 ? -_ticks : _ticks);

        public override bool Equals(object? value) => value is TimeSpan other && Equals(other);
        public bool Equals(TimeSpan value) => _ticks == value._ticks;
        public static bool Equals(TimeSpan left, TimeSpan right) => left == right;
        public override int GetHashCode() => _ticks.GetHashCode();

        public static TimeSpan FromDays(int value) => FromUnits(value, TicksPerDay, MinDays, MaxDays);
        public static TimeSpan FromHours(int value) => FromUnits(value, TicksPerHour, MinHours, MaxHours);
        public static TimeSpan FromMinutes(long value) => FromUnits(value, TicksPerMinute, MinMinutes, MaxMinutes);
        public static TimeSpan FromSeconds(long value) => FromUnits(value, TicksPerSecond, MinSeconds, MaxSeconds);
        public static TimeSpan FromMilliseconds(long value) => FromUnits(value, TicksPerMillisecond, MinMilliseconds, MaxMilliseconds);
        public static TimeSpan FromMicroseconds(long value) => FromUnits(value, TicksPerMicrosecond, MinMicroseconds, MaxMicroseconds);

        public static TimeSpan FromDays(int days, int hours = 0, long minutes = (long)0,
            long seconds = (long)0, long milliseconds = (long)0, long microseconds = (long)0) =>
            FromMicroseconds((Int128)days * MicrosecondsPerDay
                + (Int128)hours * MicrosecondsPerHour
                + (Int128)minutes * MicrosecondsPerMinute
                + (Int128)seconds * MicrosecondsPerSecond
                + (Int128)milliseconds * MicrosecondsPerMillisecond
                + microseconds);

        public static TimeSpan FromHours(int hours, long minutes = (long)0, long seconds = (long)0,
            long milliseconds = (long)0, long microseconds = (long)0) =>
            FromMicroseconds((Int128)hours * MicrosecondsPerHour
                + (Int128)minutes * MicrosecondsPerMinute
                + (Int128)seconds * MicrosecondsPerSecond
                + (Int128)milliseconds * MicrosecondsPerMillisecond
                + microseconds);

        public static TimeSpan FromMinutes(long minutes, long seconds = (long)0,
            long milliseconds = (long)0, long microseconds = (long)0) =>
            FromMicroseconds((Int128)minutes * MicrosecondsPerMinute
                + (Int128)seconds * MicrosecondsPerSecond
                + (Int128)milliseconds * MicrosecondsPerMillisecond
                + microseconds);

        public static TimeSpan FromSeconds(long seconds, long milliseconds = (long)0, long microseconds = (long)0) =>
            FromMicroseconds((Int128)seconds * MicrosecondsPerSecond
                + (Int128)milliseconds * MicrosecondsPerMillisecond
                + microseconds);

        public static TimeSpan FromMilliseconds(long milliseconds, long microseconds) =>
            FromMicroseconds((Int128)milliseconds * MicrosecondsPerMillisecond + microseconds);

        public static TimeSpan FromDays(double value) => Interval(value, TicksPerDay);
        public static TimeSpan FromHours(double value) => Interval(value, TicksPerHour);
        public static TimeSpan FromMinutes(double value) => Interval(value, TicksPerMinute);
        public static TimeSpan FromSeconds(double value) => Interval(value, TicksPerSecond);
        public static TimeSpan FromMilliseconds(double value) => Interval(value, TicksPerMillisecond);
        public static TimeSpan FromMicroseconds(double value) => Interval(value, TicksPerMicrosecond);
        public static TimeSpan FromTicks(long value) => new(value);

        public TimeSpan Negate() => -this;
        public TimeSpan Multiply(double factor) => this * factor;
        public TimeSpan Divide(double divisor) => this / divisor;
        public double Divide(TimeSpan value) => this / value;

        public override string ToString() => FormatInvariant();
        public string ToString(string? format) => FormatInvariant(format);
        public string ToString(string? format, IFormatProvider? formatProvider) => FormatInvariant(format);

        public bool TryFormat(Span<char> destination, out int charsWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? formatProvider = null)
        {
            var text = FormatInvariant(format.Length == 0 ? null : string.Create(format.ToArray()));
            return TryCopy(text, destination, out charsWritten);
        }

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

        public static TimeSpan Parse(string value)
        {
            if (value is null) throw new ArgumentNullException();
            return TryParse(value, out var result) ? result : throw new FormatException();
        }

        public static TimeSpan Parse(string value, IFormatProvider? formatProvider) => Parse(value);
        public static TimeSpan Parse(ReadOnlySpan<char> value, IFormatProvider? formatProvider = null) =>
            Parse(string.Create(value.ToArray()));

        public static TimeSpan ParseExact(string value, string format, IFormatProvider? formatProvider) =>
            ParseExact(value, format, formatProvider, System.Globalization.TimeSpanStyles.None);

        public static TimeSpan ParseExact(string value, string format, IFormatProvider? formatProvider,
            System.Globalization.TimeSpanStyles styles)
        {
            ValidateStyles(styles);
            if (value is null || format is null) throw new ArgumentNullException();
            return TryParseExact(value, format, formatProvider, styles, out var result)
                ? result
                : throw new FormatException();
        }

        public static TimeSpan ParseExact(string value, string[] formats, IFormatProvider? formatProvider) =>
            ParseExact(value, formats, formatProvider, System.Globalization.TimeSpanStyles.None);

        public static TimeSpan ParseExact(string value, string[] formats, IFormatProvider? formatProvider,
            System.Globalization.TimeSpanStyles styles)
        {
            ValidateStyles(styles);
            if (value is null || formats is null) throw new ArgumentNullException();
            return TryParseExact(value, formats, formatProvider, styles, out var result)
                ? result
                : throw new FormatException();
        }

        public static TimeSpan ParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider, System.Globalization.TimeSpanStyles styles = System.Globalization.TimeSpanStyles.None)
        {
            ValidateStyles(styles);
            return ParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), formatProvider, styles);
        }

        public static TimeSpan ParseExact(ReadOnlySpan<char> value, string[] formats,
            IFormatProvider? formatProvider, System.Globalization.TimeSpanStyles styles = System.Globalization.TimeSpanStyles.None)
        {
            ValidateStyles(styles);
            return ParseExact(string.Create(value.ToArray()), formats, formatProvider, styles);
        }

        public static bool TryParse(string? value, out TimeSpan result)
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
            if (value.Length == 0) return false;

            var offset = 0;
            var negative = value[0] == '-';
            if (negative && ++offset == value.Length) return false;
            var firstColon = value.IndexOf(':', offset);
            if (firstColon < 0) return false;
            var daySeparator = value.IndexOf('.', offset);
            var days = 0;
            var hourOffset = offset;
            if (daySeparator >= 0 && daySeparator < firstColon)
            {
                if (!TryReadVariableDigits(value, offset, daySeparator, out days)) return false;
                hourOffset = daySeparator + 1;
            }
            if (firstColon - hourOffset != 2 ||
                !InvariantDateTimeText.TryReadDigits(value, hourOffset, 2, out var hours) ||
                !InvariantDateTimeText.TryReadDigits(value, firstColon + 1, 2, out var minutes) ||
                !InvariantDateTimeText.HasSeparator(value, firstColon + 3, ':') ||
                !InvariantDateTimeText.TryReadDigits(value, firstColon + 4, 2, out var seconds) ||
                hours >= 24 || minutes >= 60 || seconds >= 60)
            {
                return false;
            }
            var endOfSeconds = firstColon + 6;
            var fraction = 0L;
            if (endOfSeconds < value.Length &&
                (value[endOfSeconds] != '.' ||
                 !InvariantDateTimeText.TryReadFraction(value, endOfSeconds + 1, value.Length, out fraction)))
            {
                return false;
            }
            try
            {
                var magnitude = checked((ulong)days * (ulong)TicksPerDay
                    + (ulong)hours * (ulong)TicksPerHour
                    + (ulong)minutes * (ulong)TicksPerMinute
                    + (ulong)seconds * (ulong)TicksPerSecond
                    + (ulong)fraction);
                var limit = negative ? 0x8000000000000000UL : 0x7fffffffffffffffUL;
                if (magnitude > limit) return false;
                var ticks = negative
                    ? magnitude == 0x8000000000000000UL ? long.MinValue : -(long)magnitude
                    : (long)magnitude;
                result = new TimeSpan(ticks);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryParse(string? value, IFormatProvider? formatProvider, out TimeSpan result) =>
            TryParse(value, out result);
        public static bool TryParse(ReadOnlySpan<char> value, out TimeSpan result) =>
            TryParse(string.Create(value.ToArray()), out result);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? formatProvider, out TimeSpan result) =>
            TryParse(value, out result);

        public static bool TryParseExact(string? value, string? format, IFormatProvider? formatProvider,
            out TimeSpan result) =>
            TryParseExact(value, format, formatProvider, System.Globalization.TimeSpanStyles.None, out result);

        public static bool TryParseExact(string? value, string? format, IFormatProvider? formatProvider,
            System.Globalization.TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            result = default;
            if (value is null || format is null || !IsStandardFormat(format)) return false;
            if (!TryParse(value, out result)) return false;
            if ((styles & System.Globalization.TimeSpanStyles.AssumeNegative) != 0 && result._ticks > 0)
            {
                result = -result;
            }
            return true;
        }

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? formatProvider,
            out TimeSpan result) =>
            TryParseExact(value, formats, formatProvider, System.Globalization.TimeSpanStyles.None, out result);

        public static bool TryParseExact(string? value, string?[]? formats, IFormatProvider? formatProvider,
            System.Globalization.TimeSpanStyles styles, out TimeSpan result)
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

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider, out TimeSpan result) =>
            TryParseExact(value, format, formatProvider, System.Globalization.TimeSpanStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, ReadOnlySpan<char> format,
            IFormatProvider? formatProvider, System.Globalization.TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            return TryParseExact(string.Create(value.ToArray()), string.Create(format.ToArray()), formatProvider, styles, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            IFormatProvider? formatProvider, out TimeSpan result) =>
            TryParseExact(value, formats, formatProvider, System.Globalization.TimeSpanStyles.None, out result);

        public static bool TryParseExact(ReadOnlySpan<char> value, string?[]? formats,
            IFormatProvider? formatProvider, System.Globalization.TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            return TryParseExact(string.Create(value.ToArray()), formats, formatProvider, styles, out result);
        }

        public static TimeSpan operator +(TimeSpan value) => value;
        public static TimeSpan operator +(TimeSpan left, TimeSpan right) => new(checked(left._ticks + right._ticks));
        public static TimeSpan operator -(TimeSpan value) => value._ticks == MinTicks
            ? throw new OverflowException()
            : new(-value._ticks);
        public static TimeSpan operator -(TimeSpan left, TimeSpan right) => new(checked(left._ticks - right._ticks));
        public static TimeSpan operator *(TimeSpan value, double factor) =>
            IntervalFromDoubleTicks(double.IsNaN(factor) ? throw new ArgumentException() : Math.Round(value._ticks * factor));
        public static TimeSpan operator *(double factor, TimeSpan value) => value * factor;
        public static TimeSpan operator /(TimeSpan value, double divisor) =>
            IntervalFromDoubleTicks(double.IsNaN(divisor) ? throw new ArgumentException() : Math.Round(value._ticks / divisor));
        public static double operator /(TimeSpan left, TimeSpan right) => (double)left._ticks / right._ticks;
        public static bool operator ==(TimeSpan left, TimeSpan right) => left._ticks == right._ticks;
        public static bool operator !=(TimeSpan left, TimeSpan right) => left._ticks != right._ticks;
        public static bool operator <(TimeSpan left, TimeSpan right) => left._ticks < right._ticks;
        public static bool operator <=(TimeSpan left, TimeSpan right) => left._ticks <= right._ticks;
        public static bool operator >(TimeSpan left, TimeSpan right) => left._ticks > right._ticks;
        public static bool operator >=(TimeSpan left, TimeSpan right) => left._ticks >= right._ticks;

        private static TimeSpan FromMicroseconds(Int128 value)
        {
            if (value > (Int128)MaxMicroseconds || value < (Int128)MinMicroseconds)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new((long)value * TicksPerMicrosecond);
        }

        private static TimeSpan FromUnits(long value, long ticksPerUnit, long minUnits, long maxUnits) =>
            value > maxUnits || value < minUnits
                ? throw new ArgumentOutOfRangeException()
                : new(value * ticksPerUnit);

        private static TimeSpan Interval(double value, double scale)
        {
            if (double.IsNaN(value)) throw new ArgumentException();
            return IntervalFromDoubleTicks(value * scale);
        }

        private static TimeSpan IntervalFromDoubleTicks(double ticks)
        {
            if (ticks > MaxTicks || ticks < MinTicks || double.IsNaN(ticks))
            {
                throw new OverflowException();
            }
            return ticks == MaxTicks ? MaxValue : new((long)ticks);
        }

        private static void ValidateStyles(System.Globalization.TimeSpanStyles styles)
        {
            if ((styles & ~System.Globalization.TimeSpanStyles.AssumeNegative) != 0)
            {
                throw new ArgumentException();
            }
        }

        internal static long TimeToTicks(int hour, int minute, int second)
        {
            var totalSeconds = (long)hour * SecondsPerHour + (long)minute * SecondsPerMinute + second;
            if (totalSeconds > MaxSeconds || totalSeconds < MinSeconds)
            {
                throw new ArgumentOutOfRangeException();
            }
            return totalSeconds * TicksPerSecond;
        }

        private string FormatInvariant(string? format = null)
        {
            if (format is not null && format.Length != 0 && format != "c" && format != "g" && format != "G")
            {
                throw new FormatException();
            }
            var negative = _ticks < 0;
            var magnitude = negative ? unchecked((ulong)(-_ticks)) : (ulong)_ticks;
            var days = magnitude / (ulong)TicksPerDay;
            var hours = magnitude / (ulong)TicksPerHour % 24;
            var minutes = magnitude / (ulong)TicksPerMinute % 60;
            var seconds = magnitude / (ulong)TicksPerSecond % 60;
            var fraction = (long)(magnitude % (ulong)TicksPerSecond);
            return (negative ? "-" : string.Empty)
                + (days == 0 ? string.Empty : days.ToString() + ".")
                + InvariantDateTimeText.TwoDigits((int)hours) + ":"
                + InvariantDateTimeText.TwoDigits((int)minutes) + ":"
                + InvariantDateTimeText.TwoDigits((int)seconds)
                + InvariantDateTimeText.FixedFraction(fraction);
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

        private static bool TryReadVariableDigits(string value, int offset, int end, out int result)
        {
            result = 0;
            if (offset == end) return false;
            for (var index = offset; index < end; index++)
            {
                var digit = value[index] - '0';
                if ((uint)digit > 9 || result > (int.MaxValue - digit) / 10)
                {
                    result = 0;
                    return false;
                }
                result = result * 10 + digit;
            }
            return true;
        }

        private static bool IsWhiteSpace(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        private static bool IsStandardFormat(string format) => format is "c" or "g" or "G";
    }
}

namespace System.Globalization
{
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    [Flags]
    public enum TimeSpanStyles
    {
        None = 0x00000000,
        AssumeNegative = 0x00000001,
    }
}
