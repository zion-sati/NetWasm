// Adapted from dotnet/runtime System.Private.CoreLib TimeOnly.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

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
            if ((uint)hour >= 24)
            {
                throw new ArgumentOutOfRangeException(nameof(hour));
            }
            if ((uint)minute >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(minute));
            }
            if ((uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(second));
            }
            if ((uint)millisecond >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecond));
            }
            _ticks = hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond
                + millisecond * TimeSpan.TicksPerMillisecond;
        }

        public TimeOnly(int hour, int minute, int second, int millisecond, int microsecond)
        {
            if ((uint)hour >= 24)
            {
                throw new ArgumentOutOfRangeException(nameof(hour));
            }
            if ((uint)minute >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(minute));
            }
            if ((uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(second));
            }
            if ((uint)millisecond >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecond));
            }
            if ((uint)microsecond >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(microsecond));
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

        // Upstream's: the time as a DateTime on 0001-01-01.
        internal DateTime ToDateTime() => new DateTime(_ticks);
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
            var time = (ulong)_ticks;
            var startTicks = (ulong)start._ticks;
            var endTicks = (ulong)end._ticks;

            return startTicks <= endTicks
                ? time - startTicks < endTicks - startTicks
                : time - endTicks >= startTicks - endTicks;
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
        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is TimeOnly other && Equals(other);
        public override int GetHashCode() => unchecked((int)_ticks) ^ (int)(_ticks >> 32);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int hour, out int minute)
        {
            hour = Hour;
            minute = Minute;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int hour, out int minute, out int second)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int hour, out int minute, out int second, out int millisecond)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
            millisecond = Millisecond;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(
            out int hour, out int minute, out int second, out int millisecond, out int microsecond)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
            millisecond = Millisecond;
            microsecond = Microsecond;
        }
        // Text members ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
        private const ParseFlags ParseFlagsTimeMask = ParseFlags.HaveYear | ParseFlags.HaveMonth | ParseFlags.HaveDay | ParseFlags.HaveDate | ParseFlags.TimeZoneUsed |
                                                      ParseFlags.TimeZoneUtc | ParseFlags.ParsedMonthName | ParseFlags.CaptureOffset | ParseFlags.UtcSortPattern;

        /// <summary>
        /// Converts a memory span that contains string representation of a time to its TimeOnly equivalent by using culture-specific format information and a formatting style.
        /// </summary>
        /// <param name="s">The memory span that contains the string to parse.</param>
        /// <param name="provider">An object that supplies culture-specific format information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by provider and styles.</returns>
        /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)" />
        public static TimeOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider = default, DateTimeStyles style = DateTimeStyles.None)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind result = TryParseInternal(s, provider, style, out TimeOnly timeOnly);
            if (result != ParseFailureKind.None)
            {
                ThrowOnError(result, s);
            }

            return timeOnly;
        }

        private const string OFormat = "HH':'mm':'ss'.'fffffff";
        private const string RFormat = "HH':'mm':'ss";

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a time to convert.</param>
        /// <param name="format">A span containing the characters that represent a format specifier that defines the required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format, provider, and style.</returns>
        public static TimeOnly ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format, IFormatProvider? provider = default, DateTimeStyles style = DateTimeStyles.None)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind result = TryParseExactInternal(s, format, provider, style, out TimeOnly timeOnly);
            if (result != ParseFailureKind.None)
            {
                ThrowOnError(result, s);
            }

            return timeOnly;
        }

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified array of formats.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a time to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format, provider, and style.</returns>
        public static TimeOnly ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats) => ParseExact(s, formats, null, DateTimeStyles.None);

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified array of formats, culture-specific format information, and style.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a time to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format, provider, and style.</returns>
        public static TimeOnly ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            if (formats == null)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            // Validate formats array for null/empty entries upfront
            for (int i = 0; i < formats.Length; i++)
            {
                if (string.IsNullOrEmpty(formats[i]))
                {
                    throw new FormatException(SR.Argument_BadFormatSpecifier);
                }
            }

            ParseFailureKind result = TryParseExactInternal(s, formats, provider, style, out TimeOnly timeOnly);
            if (result != ParseFailureKind.None)
            {
                ThrowOnError(result, s);
            }

            return timeOnly;
        }

        /// <summary>
        /// Converts a string that contains string representation of a time to its TimeOnly equivalent by using the conventions of the current culture.
        /// </summary>
        /// <param name="s">The string that contains the string to parse.</param>
        /// <returns>An object that is equivalent to the time contained in s.</returns>
        public static TimeOnly Parse(string s) => Parse(s, null, DateTimeStyles.None);

        /// <summary>
        /// Converts a string that contains string representation of a time to its TimeOnly equivalent by using culture-specific format information and a formatting style.
        /// </summary>
        /// <param name="s">The string that contains the string to parse.</param>
        /// <param name="provider">An object that supplies culture-specific format information about s.</param>
        /// <param name="style">A bitwise combination of the enumeration values that indicates the style elements that can be present in s for the parse operation to succeed, and that defines how to interpret the parsed date. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by provider and styles.</returns>
        public static TimeOnly Parse(string s, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return Parse(s.AsSpan(), provider, style);
        }

        /// <summary>
        /// Converts the specified string representation of a time to its TimeOnly equivalent using the specified format.
        /// The format of the string representation must match the specified format exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a time to convert.</param>
        /// <param name="format">A string that represent a format specifier that defines the required format of s.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format.</returns>
        public static TimeOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string format) => ParseExact(s, format, null, DateTimeStyles.None);

        /// <summary>
        /// Converts the specified string representation of a time to its TimeOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a time to convert.</param>
        /// <param name="format">A string containing the characters that represent a format specifier that defines the required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of the enumeration values that provides additional information about s, about style elements that may be present in s, or about the conversion from s to a TimeOnly value. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format, provider, and style.</returns>
        public static TimeOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string format, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            if (format == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.format);
            return ParseExact(s.AsSpan(), format.AsSpan(), provider, style);
        }

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified array of formats.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a time to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format, provider, and style.</returns>
        public static TimeOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats) => ParseExact(s, formats, null, DateTimeStyles.None);

        /// <summary>
        /// Converts the specified string representation of a time to its TimeOnly equivalent using the specified array of formats, culture-specific format information, and style.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a time to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the time contained in s, as specified by format, provider, and style.</returns>
        public static TimeOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return ParseExact(s.AsSpan(), formats, provider, style);
        }

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing the time to convert.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<char> s, out TimeOnly result) => TryParse(s, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified array of formats, culture-specific format information, and style. And returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a time to convert.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse(ReadOnlySpan{char}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind failureKind = TryParseInternal(s, provider, style, out result);
            return failureKind == ParseFailureKind.None;
        }
        private static ParseFailureKind TryParseInternal(ReadOnlySpan<char> s, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            DateTimeResult dtResult = default;

            dtResult.Init(s);

            if (!DateTimeParse.TryParse(s, DateTimeFormatInfo.GetInstance(provider), style, ref dtResult))
            {
                result = default;
                return ParseFailureKind.Format_BadTimeOnly;
            }

            if ((dtResult.flags & ParseFlagsTimeMask) != 0)
            {
                result = default;
                return ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts;
            }

            result = FromDateTime(dtResult.parsedDate);
            return ParseFailureKind.None;
        }

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified format and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing a time to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a time that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format, out TimeOnly result) => TryParseExact(s, format, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing a time to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of one or more enumeration values that indicate the permitted format of s.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a time that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind failureKind = TryParseExactInternal(s, format, provider, style, out result);
            if (failureKind != ParseFailureKind.None)
            {
                if (failureKind == ParseFailureKind.Argument_BadFormatSpecifier)
                {
                    throw new FormatException(SR.Argument_BadFormatSpecifier);
                }
                return false;
            }
            return true;
        }

        private static ParseFailureKind TryParseExactInternal(ReadOnlySpan<char> s, ReadOnlySpan<char> format, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if (format.Length == 1)
            {
                switch (format[0] | 0x20)
                {
                    case 'o':
                        format = OFormat;
                        provider = DateTimeFormat.InvariantFormatInfo;
                        break;

                    case 'r':
                        format = RFormat;
                        provider = DateTimeFormat.InvariantFormatInfo;
                        break;
                }
            }

            DateTimeResult dtResult = default;
            dtResult.Init(s);

            if (!DateTimeParse.TryParseExact(s, format, DateTimeFormatInfo.GetInstance(provider), style, ref dtResult))
            {
                result = default;
                return dtResult.failure == ParseFailureKind.Argument_BadFormatSpecifier
                    ? ParseFailureKind.Argument_BadFormatSpecifier
                    : ParseFailureKind.Format_BadTimeOnly;
            }

            if ((dtResult.flags & ParseFlagsTimeMask) != 0)
            {
                result = default;
                return ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts;
            }

            result = FromDateTime(dtResult.parsedDate);
            return ParseFailureKind.None;
        }

        /// <summary>
        /// Converts the specified char span of a time to its TimeOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The span containing the string to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats, out TimeOnly result) => TryParseExact(s, formats, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified char span of a time to its TimeOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The span containing the string to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that defines how to interpret the parsed time. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            if (formats == null)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            // Validate formats array for null/empty entries upfront
            for (int i = 0; i < formats.Length; i++)
            {
                if (string.IsNullOrEmpty(formats[i]))
                {
                    throw new FormatException(SR.Argument_BadFormatSpecifier);
                }
            }

            return TryParseExactInternal(s, formats, provider, style, out result) == ParseFailureKind.None;
        }

        private static ParseFailureKind TryParseExactInternal(ReadOnlySpan<char> s, string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            DateTimeFormatInfo dtfi = DateTimeFormatInfo.GetInstance(provider);

            for (int i = 0; i < formats!.Length; i++)
            {
                DateTimeFormatInfo dtfiToUse = dtfi;
                string? format = formats[i];

                if (format!.Length == 1)
                {
                    switch (format[0] | 0x20)
                    {
                        case 'o':
                            format = OFormat;
                            dtfiToUse = DateTimeFormat.InvariantFormatInfo;
                            break;

                        case 'r':
                            format = RFormat;
                            dtfiToUse = DateTimeFormat.InvariantFormatInfo;
                            break;
                    }
                }

                // Create a new result each time to ensure the runs are independent. Carry through
                // flags from the caller and return the result.
                DateTimeResult dtResult = default;
                dtResult.Init(s);
                if (DateTimeParse.TryParseExact(s, format, dtfiToUse, style, ref dtResult) && ((dtResult.flags & ParseFlagsTimeMask) == 0))
                {
                    result = FromDateTime(dtResult.parsedDate);
                    return ParseFailureKind.None;
                }

                if (dtResult.failure == ParseFailureKind.Argument_BadFormatSpecifier)
                {
                    result = default;
                    return ParseFailureKind.Argument_BadFormatSpecifier;
                }
            }

            result = default;
            return ParseFailureKind.Format_BadTimeOnly;
        }

        /// <summary>
        /// Converts the specified string representation of a time to its TimeOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters representing the time to convert.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, out TimeOnly result) => TryParse(s, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified string representation of a time to its TimeOnly equivalent using the specified array of formats, culture-specific format information, and style. And returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a time to convert.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if (s == null)
            {
                result = default;
                return false;
            }

            return TryParse(s.AsSpan(), provider, style, out result);
        }

        /// <summary>
        /// Converts the specified string representation of a time to its TimeOnly equivalent using the specified format and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters representing a time to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a time that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format, out TimeOnly result) => TryParseExact(s, format, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified span representation of a time to its TimeOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing a time to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of one or more enumeration values that indicate the permitted format of s.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a time that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if (s == null || format == null)
            {
                result = default;
                return false;
            }

            return TryParseExact(s.AsSpan(), format.AsSpan(), provider, style, out result);
        }

        /// <summary>
        /// Converts the specified string of a time to its TimeOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The string containing time to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="result">When this method returns, contains the timeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats, out TimeOnly result) => TryParseExact(s, formats, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified string of a time to its TimeOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The string containing the time to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that defines how to interpret the parsed date. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the TimeOnly value equivalent to the time contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a time. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out TimeOnly result)
        {
            if (s == null)
            {
                result = default;
                return false;
            }

            return TryParseExact(s.AsSpan(), formats, provider, style, out result);
        }

        private static void ThrowOnError(ParseFailureKind result, ReadOnlySpan<char> s)
        {
            Debug.Assert(result != ParseFailureKind.None);
            switch (result)
            {
                case ParseFailureKind.Argument_InvalidDateStyles: throw new ArgumentException(SR.Argument_InvalidDateStyles, "style");
                case ParseFailureKind.Argument_BadFormatSpecifier: throw new FormatException(SR.Argument_BadFormatSpecifier);
                case ParseFailureKind.Format_BadTimeOnly: throw new FormatException(SR.Format(SR.Format_BadTimeOnly, s.ToString()));
                default:
                    Debug.Assert(result == ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts);
                    throw new FormatException(SR.Format(SR.Format_DateTimeOnlyContainsNoneDateParts, s.ToString(), nameof(TimeOnly)));
            }
        }

        /// <summary>
        /// Converts the value of the current TimeOnly object to its equivalent long date string representation.
        /// </summary>
        /// <returns>A string that contains the long time string representation of the current TimeOnly object.</returns>
        public string ToLongTimeString() => ToString("T");

        /// <summary>
        /// Converts the value of the current TimeOnly object to its equivalent short time string representation.
        /// </summary>
        /// <returns>A string that contains the short time string representation of the current TimeOnly object.</returns>
        public string ToShortTimeString() => ToString();

        /// <summary>
        /// Converts the value of the current TimeOnly object to its equivalent string representation using the formatting conventions of the current culture.
        /// The TimeOnly object will be formatted in short form.
        /// </summary>
        /// <returns>A string that contains the short time string representation of the current TimeOnly object.</returns>
        // NetWasm: see InvariantDateTimeDefaults.
        public override string ToString() =>
            DateTimeFormatInfo.s_currentInfo is null
                ? InvariantDateTimeDefaults.ShortTime(ToDateTime())
                : DateTimeFormat.Format(ToDateTime(), "t", null);

        /// <summary>
        /// Converts the value of the current TimeOnly object to its equivalent string representation using the specified format and the formatting conventions of the current culture.
        /// </summary>
        /// <param name="format">A standard or custom time format string.</param>
        /// <returns>A string representation of value of the current TimeOnly object as specified by format.</returns>
        /// <remarks>The accepted standard formats are 'r', 'R', 'o', 'O', 't' and 'T'. </remarks>
        public string ToString([StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format) => ToString(format, null);

        /// <summary>
        /// Converts the value of the current TimeOnly object to its equivalent string representation using the specified culture-specific format information.
        /// </summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <returns>A string representation of value of the current TimeOnly object as specified by provider.</returns>
        public string ToString(IFormatProvider? provider) => DateTimeFormat.Format(ToDateTime(), "t", provider);

        /// <summary>
        /// Converts the value of the current TimeOnly object to its equivalent string representation using the specified culture-specific format information.
        /// </summary>
        /// <param name="format">A standard or custom time format string.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <returns>A string representation of value of the current TimeOnly object as specified by format and provider.</returns>
        /// <remarks>The accepted standard formats are 'r', 'R', 'o', 'O', 't' and 'T'. </remarks>
        public string ToString([StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format, IFormatProvider? provider)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = "t";
            }

            if (format.Length == 1)
            {
                return (format[0] | 0x20) switch
                {
                    'o' => string.Create(16, this, (destination, value) =>
                           {
                               DateTimeFormat.TryFormatTimeOnlyO(value, destination, out int charsWritten);
                               Debug.Assert(charsWritten == destination.Length);
                           }),

                    'r' => string.Create(8, this, (destination, value) =>
                           {
                               DateTimeFormat.TryFormatTimeOnlyR(value, destination, out int charsWritten);
                               Debug.Assert(charsWritten == destination.Length);
                           }),

                    't' => DateTimeFormat.Format(ToDateTime(), format, provider),

                    _ => throw new FormatException(SR.Format_InvalidString),
                };
            }

            DateTimeFormat.IsValidCustomTimeOnlyFormat(format.AsSpan(), throwOnError: true);
            return DateTimeFormat.Format(ToDateTime(), format, provider);
        }

        /// <summary>
        /// Tries to format the value of the current TimeOnly instance into the provided span of characters.
        /// </summary>
        /// <param name="destination">When this method returns, this instance's value formatted as a span of characters.</param>
        /// <param name="charsWritten">When this method returns, the number of characters that were written in destination.</param>
        /// <param name="format">A span containing the characters that represent a standard or custom format string that defines the acceptable format for destination.</param>
        /// <param name="provider">An optional object that supplies culture-specific formatting information for destination.</param>
        /// <returns>true if the formatting was successful; otherwise, false.</returns>
        /// <remarks>The accepted standard formats are 'r', 'R', 'o', 'O', 't' and 'T'. </remarks>
        public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            TryFormatCore(destination, out charsWritten, format, provider);

        /// <inheritdoc cref="IUtf8SpanFormattable.TryFormat" />
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            TryFormatCore(utf8Destination, out bytesWritten, format, provider);

        private bool TryFormatCore<TChar>(Span<TChar> destination, out int written, [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format, IFormatProvider? provider) where TChar : unmanaged, IUtfChar<TChar>
        {
            if (format.Length == 0)
            {
                format = "t";
            }

            if (format.Length == 1)
            {
                switch (format[0] | 0x20)
                {
                    case 'o':
                        return DateTimeFormat.TryFormatTimeOnlyO(this, destination, out written);

                    case 'r':
                        return DateTimeFormat.TryFormatTimeOnlyR(this, destination, out written);

                    case 't':
                        return DateTimeFormat.TryFormat(ToDateTime(), destination, out written, format, provider);

                    default:
                        ThrowHelper.ThrowFormatException_BadFormatSpecifier();
                        break;
                }
            }

            if (!DateTimeFormat.IsValidCustomTimeOnlyFormat(format, throwOnError: false))
            {
                throw new FormatException(SR.Format(SR.Format_DateTimeOnlyContainsNoneDateParts, format.ToString(), nameof(TimeOnly)));
            }

            return DateTimeFormat.TryFormat(ToDateTime(), destination, out written, format, provider);
        }

        //
        // IParsable
        //

        /// <inheritdoc cref="IParsable{TSelf}.Parse(string, IFormatProvider?)" />
        public static TimeOnly Parse(string s, IFormatProvider? provider) => Parse(s, provider, DateTimeStyles.None);

        /// <inheritdoc cref="IParsable{TSelf}.TryParse(string?, IFormatProvider?, out TSelf)" />
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TimeOnly result) => TryParse(s, provider, DateTimeStyles.None, out result);

        //
        // ISpanParsable
        //

        /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)" />
        public static TimeOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, provider, DateTimeStyles.None);

        /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse(ReadOnlySpan{char}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TimeOnly result) => TryParse(s, provider, DateTimeStyles.None, out result);

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

    }
}
