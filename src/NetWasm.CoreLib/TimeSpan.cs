// Adapted from dotnet/runtime System.Private.CoreLib TimeSpan.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;

    [Serializable]
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

        // Text members ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
        private static void ValidateStyles(TimeSpanStyles style)
        {
            if (style is not TimeSpanStyles.None and not TimeSpanStyles.AssumeNegative)
            {
                ThrowHelper.ThrowArgumentException_InvalidTimeSpanStyles();
            }
        }
        public static TimeSpan Parse(string s)
        {
            /* Constructs a TimeSpan from a string.  Leading and trailing white space characters are allowed. */
            ArgumentNullException.ThrowIfNull(s, ExceptionArgument.input);
            return TimeSpanParse.Parse(s, null);
        }
        public static TimeSpan Parse(string input, IFormatProvider? formatProvider)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }
            return TimeSpanParse.Parse(input, formatProvider);
        }
        public static TimeSpan Parse(ReadOnlySpan<char> input, IFormatProvider? formatProvider = null) => TimeSpanParse.Parse(input, formatProvider);
        public static TimeSpan ParseExact(string input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string format, IFormatProvider? formatProvider)
        {
            ArgumentNullException.ThrowIfNull(input, ExceptionArgument.input);
            ArgumentNullException.ThrowIfNull(format, ExceptionArgument.format);
            return TimeSpanParse.ParseExact(input, format, formatProvider, TimeSpanStyles.None);
        }
        public static TimeSpan ParseExact(string input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string[] formats, IFormatProvider? formatProvider)
        {
            if (input is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.input);
            }
            return TimeSpanParse.ParseExactMultiple(input, formats, formatProvider, TimeSpanStyles.None);
        }
        public static TimeSpan ParseExact(string input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string format, IFormatProvider? formatProvider, TimeSpanStyles styles)
        {
            ValidateStyles(styles);
            ArgumentNullException.ThrowIfNull(input, ExceptionArgument.input);
            ArgumentNullException.ThrowIfNull(format, ExceptionArgument.format);
            return TimeSpanParse.ParseExact(input, format, formatProvider, styles);
        }

        public static TimeSpan ParseExact(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles = TimeSpanStyles.None)
        {
            ValidateStyles(styles);
            return TimeSpanParse.ParseExact(input, format, formatProvider, styles);
        }
        public static TimeSpan ParseExact(string input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string[] formats, IFormatProvider? formatProvider, TimeSpanStyles styles)
        {
            ValidateStyles(styles);
            ArgumentNullException.ThrowIfNull(input, ExceptionArgument.input);
            return TimeSpanParse.ParseExactMultiple(input, formats, formatProvider, styles);
        }
        public static TimeSpan ParseExact(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string[] formats, IFormatProvider? formatProvider, TimeSpanStyles styles = TimeSpanStyles.None)
        {
            ValidateStyles(styles);
            return TimeSpanParse.ParseExactMultiple(input, formats, formatProvider, styles);
        }
        public static bool TryParse([NotNullWhen(true)] string? s, out TimeSpan result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParse(s, null, out result);
        }
        public static bool TryParse(ReadOnlySpan<char> s, out TimeSpan result) => TimeSpanParse.TryParse(s, null, out result);

        public static bool TryParse([NotNullWhen(true)] string? input, IFormatProvider? formatProvider, out TimeSpan result)
        {
            if (input is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParse(input, formatProvider, out result);
        }
        public static bool TryParse(ReadOnlySpan<char> input, IFormatProvider? formatProvider, out TimeSpan result) => TimeSpanParse.TryParse(input, formatProvider, out result);
        public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string? format, IFormatProvider? formatProvider, out TimeSpan result)
        {
            if (input is null || format is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExact(input, format, formatProvider, TimeSpanStyles.None, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] ReadOnlySpan<char> format, IFormatProvider? formatProvider, out TimeSpan result)
            => TimeSpanParse.TryParseExact(input, format, formatProvider, TimeSpanStyles.None, out result);

        public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string?[]? formats, IFormatProvider? formatProvider, out TimeSpan result)
        {
            if (input is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, TimeSpanStyles.None, out result);
        }
        public static bool TryParseExact(ReadOnlySpan<char> input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string?[]? formats, IFormatProvider? formatProvider, out TimeSpan result)
            => TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, TimeSpanStyles.None, out result);

        public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string? format, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);

            if (input is null || format is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExact(input, format, formatProvider, styles, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] ReadOnlySpan<char> format, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            return TimeSpanParse.TryParseExact(input, format, formatProvider, styles, out result);
        }
        public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);

            if (input is null)
            {
                result = default;
                return false;
            }
            return TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, styles, out result);
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string?[]? formats, IFormatProvider? formatProvider, TimeSpanStyles styles, out TimeSpan result)
        {
            ValidateStyles(styles);
            return TimeSpanParse.TryParseExactMultiple(input, formats, formatProvider, styles, out result);
        }
        public override string ToString() => TimeSpanFormat.FormatC(this);
        public string ToString([StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string? format) => TimeSpanFormat.Format(this, format, null);
        public string ToString([StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] string? format, IFormatProvider? formatProvider) => TimeSpanFormat.Format(this, format, formatProvider);

        public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = null)
            => TimeSpanFormat.TryFormat(this, destination, out charsWritten, format, formatProvider);

        /// <inheritdoc cref="IUtf8SpanFormattable.TryFormat" />
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.TimeSpanFormat)] ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = null)
            => TimeSpanFormat.TryFormat(this, utf8Destination, out bytesWritten, format, formatProvider);

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

        internal static long TimeToTicks(int hour, int minute, int second)
        {
            var totalSeconds = (long)hour * SecondsPerHour + (long)minute * SecondsPerMinute + second;
            if (totalSeconds > MaxSeconds || totalSeconds < MinSeconds)
            {
                throw new ArgumentOutOfRangeException();
            }
            return totalSeconds * TicksPerSecond;
        }


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
