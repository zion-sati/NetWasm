// Adapted from dotnet/runtime System.Private.CoreLib DateOnly.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

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

        private static uint DayNumberFromDateTime(DateTime dt) => (uint)((ulong)dt.Ticks / TimeSpan.TicksPerDay);

        internal DateTime GetEquivalentDateTime() => new DateTime(_dayNumber * TimeSpan.TicksPerDay);

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
                throw new ArgumentOutOfRangeException("months");
            }
            var year = Year;
            var month = Month;
            var totalMonths = (year - 1) * 12 + month - 1 + value;
            if (totalMonths < 0 || totalMonths >= 9999 * 12)
            {
                throw new ArgumentOutOfRangeException("months");
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
            try
            {
                return AddMonths(value * 12);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        public static DateOnly FromDayNumber(int dayNumber)
        {
            if ((uint)dayNumber >= GregorianDateMath.DaysTo10000)
            {
                throw new ArgumentOutOfRangeException(nameof(dayNumber), dayNumber, null);
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
        public DateTime ToDateTime(TimeOnly time, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local)
            {
                throw new ArgumentException(null, nameof(kind));
            }

            return new DateTime(
                checked((long)_dayNumber * TimeSpan.TicksPerDay + time.Ticks), kind);
        }
        public int CompareTo(DateOnly value) => _dayNumber.CompareTo(value._dayNumber);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is DateOnly other
                ? CompareTo(other)
                : throw new ArgumentException();
        public bool Equals(DateOnly value) => _dayNumber == value._dayNumber;
        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is DateOnly other && Equals(other);
        public override int GetHashCode() => (int)_dayNumber;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int year, out int month, out int day) =>
            GetParts(out year, out month, out day);
        // Text members ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
        private const ParseFlags ParseFlagsDateMask = ParseFlags.HaveHour | ParseFlags.HaveMinute | ParseFlags.HaveSecond | ParseFlags.HaveTime | ParseFlags.TimeZoneUsed |
                                                      ParseFlags.TimeZoneUtc | ParseFlags.CaptureOffset | ParseFlags.UtcSortPattern;

        /// <summary>
        /// Converts a memory span that contains string representation of a date to its DateOnly equivalent by using culture-specific format information and a formatting style.
        /// </summary>
        /// <param name="s">The memory span that contains the string to parse.</param>
        /// <param name="provider">An object that supplies culture-specific format information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by provider and styles.</returns>
        public static DateOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider = default, DateTimeStyles style = DateTimeStyles.None)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind result = TryParseInternal(s, provider, style, out DateOnly dateOnly);
            if (result != ParseFailureKind.None)
            {
                ThrowOnError(result, s);
            }

            return dateOnly;
        }

        private const string OFormat = "yyyy'-'MM'-'dd";
        private const string RFormat = "ddd, dd MMM yyyy";

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a date to convert.</param>
        /// <param name="format">A span containing the characters that represent a format specifier that defines the required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format, provider, and style.</returns>
        public static DateOnly ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format, IFormatProvider? provider = default, DateTimeStyles style = DateTimeStyles.None)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind result = TryParseExactInternal(s, format, provider, style, out DateOnly dateOnly);

            if (result != ParseFailureKind.None)
            {
                ThrowOnError(result, s);
            }

            return dateOnly;
        }

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified array of formats.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a date to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format, provider, and style.</returns>
        public static DateOnly ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats) => ParseExact(s, formats, null, DateTimeStyles.None);

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified array of formats, culture-specific format information, and style.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a date to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format, provider, and style.</returns>
        public static DateOnly ParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
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

            ParseFailureKind result = TryParseExactInternal(s, formats, provider, style, out DateOnly dateOnly);
            if (result != ParseFailureKind.None)
            {
                ThrowOnError(result, s);
            }

            return dateOnly;
        }

        /// <summary>
        /// Converts a string that contains string representation of a date to its DateOnly equivalent by using the conventions of the current culture.
        /// </summary>
        /// <param name="s">The string that contains the string to parse.</param>
        /// <returns>An object that is equivalent to the date contained in s.</returns>
        public static DateOnly Parse(string s) => Parse(s, null, DateTimeStyles.None);

        /// <summary>
        /// Converts a string that contains string representation of a date to its DateOnly equivalent by using culture-specific format information and a formatting style.
        /// </summary>
        /// <param name="s">The string that contains the string to parse.</param>
        /// <param name="provider">An object that supplies culture-specific format information about s.</param>
        /// <param name="style">A bitwise combination of the enumeration values that indicates the style elements that can be present in s for the parse operation to succeed, and that defines how to interpret the parsed date. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by provider and styles.</returns>
        public static DateOnly Parse(string s, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return Parse(s.AsSpan(), provider, style);
        }

        /// <summary>
        /// Converts the specified string representation of a date to its DateOnly equivalent using the specified format.
        /// The format of the string representation must match the specified format exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a date to convert.</param>
        /// <param name="format">A string that represent a format specifier that defines the required format of s.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format.</returns>
        public static DateOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string format) => ParseExact(s, format, null, DateTimeStyles.None);

        /// <summary>
        /// Converts the specified string representation of a date to its DateOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a date to convert.</param>
        /// <param name="format">A string containing the characters that represent a format specifier that defines the required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of the enumeration values that provides additional information about s, about style elements that may be present in s, or about the conversion from s to a DateOnly value. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format, provider, and style.</returns>
        public static DateOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string format, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            if (format == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.format);
            return ParseExact(s.AsSpan(), format.AsSpan(), provider, style);
        }

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified array of formats.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A span containing the characters that represent a date to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format, provider, and style.</returns>
        public static DateOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats) => ParseExact(s, formats, null, DateTimeStyles.None);

        /// <summary>
        /// Converts the specified string representation of a date to its DateOnly equivalent using the specified array of formats, culture-specific format information, and style.
        /// The format of the string representation must match at least one of the specified formats exactly or an exception is thrown.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a date to convert.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <returns>An object that is equivalent to the date contained in s, as specified by format, provider, and style.</returns>
        public static DateOnly ParseExact(string s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats, IFormatProvider? provider, DateTimeStyles style = DateTimeStyles.None)
        {
            if (s == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.s);
            return ParseExact(s.AsSpan(), formats, provider, style);
        }

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing the date to convert.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<char> s, out DateOnly result) => TryParse(s, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified array of formats, culture-specific format information, and style. And returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a date to convert.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            }

            ParseFailureKind failureKind = TryParseInternal(s, provider, style, out result);
            return failureKind == ParseFailureKind.None;
        }

        private static ParseFailureKind TryParseInternal(ReadOnlySpan<char> s, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
        {
            DateTimeResult dtResult = default;
            dtResult.Init(s);

            if (!DateTimeParse.TryParse(s, DateTimeFormatInfo.GetInstance(provider), style, ref dtResult))
            {
                result = default;
                return ParseFailureKind.Format_BadDateOnly;
            }

            if ((dtResult.flags & ParseFlagsDateMask) != 0)
            {
                result = default;
                return ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts;
            }

            result = new DateOnly(DayNumberFromDateTime(dtResult.parsedDate));
            return ParseFailureKind.None;
        }

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified format and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing a date to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a date that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format, out DateOnly result) => TryParseExact(s, format, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing a date to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of one or more enumeration values that indicate the permitted format of s.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a date that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
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
        private static ParseFailureKind TryParseExactInternal(ReadOnlySpan<char> s, ReadOnlySpan<char> format, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
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
                    : ParseFailureKind.Format_BadDateOnly;
            }

            if ((dtResult.flags & ParseFlagsDateMask) != 0)
            {
                result = default;
                return ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts;
            }

            result = new DateOnly(DayNumberFromDateTime(dtResult.parsedDate));

            return ParseFailureKind.None;
        }

        /// <summary>
        /// Converts the specified char span of a date to its DateOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The span containing the string to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats, out DateOnly result) => TryParseExact(s, formats, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified char span of a date to its DateOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The span containing the string to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that defines how to interpret the parsed date. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact(ReadOnlySpan<char> s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
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

        private static ParseFailureKind TryParseExactInternal(ReadOnlySpan<char> s, string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
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
                if (DateTimeParse.TryParseExact(s, format, dtfiToUse, style, ref dtResult) && ((dtResult.flags & ParseFlagsDateMask) == 0))
                {
                    result = new DateOnly(DayNumberFromDateTime(dtResult.parsedDate));
                    return ParseFailureKind.None;
                }

                if (dtResult.failure == ParseFailureKind.Argument_BadFormatSpecifier)
                {
                    result = default;
                    return ParseFailureKind.Argument_BadFormatSpecifier;
                }
            }

            result = default;
            return ParseFailureKind.Format_BadDateOnly;
        }

        /// <summary>
        /// Converts the specified string representation of a date to its DateOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters representing the date to convert.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, out DateOnly result) => TryParse(s, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified string representation of a date to its DateOnly equivalent using the specified array of formats, culture-specific format information, and style. And returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters that represent a date to convert.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicates the permitted format of s. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is empty string, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
        {
            if (s == null)
            {
                result = default;
                return false;
            }

            return TryParse(s.AsSpan(), provider, style, out result);
        }

        /// <summary>
        /// Converts the specified string representation of a date to its DateOnly equivalent using the specified format and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A string containing the characters representing a date to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a date that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format, out DateOnly result) => TryParseExact(s, format, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified span representation of a date to its DateOnly equivalent using the specified format, culture-specific format information, and style.
        /// The format of the string representation must match the specified format exactly. The method returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">A span containing the characters representing a date to convert.</param>
        /// <param name="format">The required format of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of one or more enumeration values that indicate the permitted format of s.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s is empty string, or does not contain a date that correspond to the pattern specified in format. This parameter is passed uninitialized.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
        {
            if (s == null || format == null)
            {
                result = default;
                return false;
            }

            return TryParseExact(s.AsSpan(), format.AsSpan(), provider, style, out result);
        }

        /// <summary>
        /// Converts the specified string of a date to its DateOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The string containing date to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats, out DateOnly result) => TryParseExact(s, formats, null, DateTimeStyles.None, out result);

        /// <summary>
        /// Converts the specified string of a date to its DateOnly equivalent and returns a value that indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="s">The string containing the date to parse.</param>
        /// <param name="formats">An array of allowable formats of s.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about s.</param>
        /// <param name="style">A bitwise combination of enumeration values that defines how to interpret the parsed date. A typical value to specify is None.</param>
        /// <param name="result">When this method returns, contains the DateOnly value equivalent to the date contained in s, if the conversion succeeded, or MinValue if the conversion failed. The conversion fails if the s parameter is Empty, or does not contain a valid string representation of a date. This parameter is passed uninitialized.</param>
        /// <returns>true if the s parameter was converted successfully; otherwise, false.</returns>
        public static bool TryParseExact([NotNullWhen(true)] string? s, [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats, IFormatProvider? provider, DateTimeStyles style, out DateOnly result)
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
                case ParseFailureKind.Format_BadDateOnly: throw new FormatException(SR.Format(SR.Format_BadDateOnly, s.ToString()));
                default:
                    Debug.Assert(result == ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts);
                    throw new FormatException(SR.Format(SR.Format_DateTimeOnlyContainsNoneDateParts, s.ToString(), nameof(DateOnly)));
            }
        }

        /// <summary>
        /// Converts the value of the current DateOnly object to its equivalent long date string representation.
        /// </summary>
        /// <returns>A string that contains the long date string representation of the current DateOnly object.</returns>
        public string ToLongDateString() => ToString("D");

        /// <summary>
        /// Converts the value of the current DateOnly object to its equivalent short date string representation.
        /// </summary>
        /// <returns>A string that contains the short date string representation of the current DateOnly object.</returns>
        public string ToShortDateString() => ToString();

        /// <summary>
        /// Converts the value of the current DateOnly object to its equivalent string representation using the formatting conventions of the current culture.
        /// The DateOnly object will be formatted in short form.
        /// </summary>
        /// <returns>A string that contains the short date string representation of the current DateOnly object.</returns>
        // NetWasm: see InvariantDateTimeDefaults.
        public override string ToString() =>
            DateTimeFormatInfo.s_currentInfo is null
                ? InvariantDateTimeDefaults.ShortDate(GetEquivalentDateTime())
                : DateTimeFormat.Format(GetEquivalentDateTime(), "d", null);

        /// <summary>
        /// Converts the value of the current DateOnly object to its equivalent string representation using the specified format and the formatting conventions of the current culture.
        /// </summary>
        /// <param name="format">A standard or custom date format string.</param>
        /// <returns>A string representation of value of the current DateOnly object as specified by format.</returns>
        public string ToString([StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format) => ToString(format, null);

        /// <summary>
        /// Converts the value of the current DateOnly object to its equivalent string representation using the specified culture-specific format information.
        /// </summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <returns>A string representation of value of the current DateOnly object as specified by provider.</returns>
        public string ToString(IFormatProvider? provider) => DateTimeFormat.Format(GetEquivalentDateTime(), "d", provider);

        /// <summary>
        /// Converts the value of the current DateOnly object to its equivalent string representation using the specified culture-specific format information.
        /// </summary>
        /// <param name="format">A standard or custom date format string.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <returns>A string representation of value of the current DateOnly object as specified by format and provider.</returns>
        public string ToString([StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format, IFormatProvider? provider)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = "d";
            }

            if (format.Length == 1)
            {
                return (format[0] | 0x20) switch
                {
                    'o' => string.Create(10, this, (destination, value) =>
                           {
                               DateTimeFormat.TryFormatDateOnlyO(value, destination, out int charsWritten);
                               Debug.Assert(charsWritten == destination.Length);
                           }),

                    'r' => string.Create(16, this, (destination, value) =>
                           {
                               DateTimeFormat.TryFormatDateOnlyR(value, destination, out int charsWritten);
                               Debug.Assert(charsWritten == destination.Length);
                           }),

                    'm' or 'd' or 'y' => DateTimeFormat.Format(GetEquivalentDateTime(), format, provider),

                    _ => throw new FormatException(SR.Format_InvalidString),
                };
            }

            DateTimeFormat.IsValidCustomDateOnlyFormat(format.AsSpan(), throwOnError: true);
            return DateTimeFormat.Format(GetEquivalentDateTime(), format, provider);
        }

        /// <summary>
        /// Tries to format the value of the current DateOnly instance into the provided span of characters.
        /// </summary>
        /// <param name="destination">When this method returns, this instance's value formatted as a span of characters.</param>
        /// <param name="charsWritten">When this method returns, the number of characters that were written in destination.</param>
        /// <param name="format">A span containing the characters that represent a standard or custom format string that defines the acceptable format for destination.</param>
        /// <param name="provider">An optional object that supplies culture-specific formatting information for destination.</param>
        /// <returns>true if the formatting was successful; otherwise, false.</returns>
        public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            TryFormatCore(destination, out charsWritten, format, provider);

        /// <inheritdoc cref="IUtf8SpanFormattable.TryFormat" />
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            TryFormatCore(utf8Destination, out bytesWritten, format, provider);

        private bool TryFormatCore<TChar>(Span<TChar> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format, IFormatProvider? provider = null)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (format.Length == 0)
            {
                format = "d";
            }

            if (format.Length == 1)
            {
                switch (format[0] | 0x20)
                {
                    case 'o':
                        return DateTimeFormat.TryFormatDateOnlyO(this, destination, out charsWritten);

                    case 'r':
                        return DateTimeFormat.TryFormatDateOnlyR(this, destination, out charsWritten);

                    case 'm':
                    case 'd':
                    case 'y':
                        return DateTimeFormat.TryFormat(GetEquivalentDateTime(), destination, out charsWritten, format, provider);

                    default:
                        ThrowHelper.ThrowFormatException_BadFormatSpecifier();
                        break;
                }
            }

            if (!DateTimeFormat.IsValidCustomDateOnlyFormat(format, throwOnError: false))
            {
                throw new FormatException(SR.Format(SR.Format_DateTimeOnlyContainsNoneDateParts, format.ToString(), nameof(DateOnly)));
            }

            return DateTimeFormat.TryFormat(GetEquivalentDateTime(), destination, out charsWritten, format, provider);
        }

        //
        // IParsable
        //

        /// <inheritdoc cref="IParsable{TSelf}.Parse(string, IFormatProvider?)" />
        public static DateOnly Parse(string s, IFormatProvider? provider) => Parse(s, provider, DateTimeStyles.None);

        /// <inheritdoc cref="IParsable{TSelf}.TryParse(string?, IFormatProvider?, out TSelf)" />
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out DateOnly result) => TryParse(s, provider, DateTimeStyles.None, out result);

        //
        // ISpanParsable
        //

        /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)" />
        public static DateOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, provider, DateTimeStyles.None);

        /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse(ReadOnlySpan{char}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateOnly result) => TryParse(s, provider, DateTimeStyles.None, out result);

        public static bool operator ==(DateOnly left, DateOnly right) => left.Equals(right);
        public static bool operator !=(DateOnly left, DateOnly right) => !left.Equals(right);
        public static bool operator <(DateOnly left, DateOnly right) => left._dayNumber < right._dayNumber;
        public static bool operator <=(DateOnly left, DateOnly right) => left._dayNumber <= right._dayNumber;
        public static bool operator >(DateOnly left, DateOnly right) => left._dayNumber > right._dayNumber;
        public static bool operator >=(DateOnly left, DateOnly right) => left._dayNumber >= right._dayNumber;

        private void GetParts(out int year, out int month, out int day) =>
            GregorianDateMath.GetDateParts((int)_dayNumber, out year, out month, out day);

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
