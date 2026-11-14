// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Globalization
{
    internal static class SR
    {
        internal const string Arg_ArgumentOutOfRangeException =
            "Specified argument was out of the range of valid values.";
        internal const string Arg_ArrayZeroError =
            "Array must not be of length zero.";
        internal const string Arg_BinaryStyleNotSupported =
            "The number style AllowBinarySpecifier is not supported on floating point data types.";
        internal const string Arg_InvalidHexFloatStyle =
            "With the AllowHexSpecifier bit set in the enum bit field, the only other valid bits that can be combined into the enum value must be a subset of HexFloat (AllowLeadingWhite, AllowTrailingWhite, AllowLeadingSign, AllowDecimalPoint, and AllowExponent). AllowExponent is required when AllowHexSpecifier is specified.";
        internal const string ArgumentNull_ArrayValue =
            "Found a null value within an array.";
        internal const string ArgumentOutOfRange_Count =
            "Count must be positive and count must refer to a location within the string/array/collection.";
        internal const string ArgumentOutOfRange_IndexMustBeLess =
            "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string Argument_CompareOptionOrdinal =
            "CompareOption.Ordinal cannot be used with other options.";
        internal const string Argument_ConflictingDateTimeRoundtripStyles =
            "The DateTimeStyles value RoundtripKind cannot be used with the values AssumeLocal, AssumeUniversal or AdjustToUniversal.";
        internal const string Argument_ConflictingDateTimeStyles =
            "The DateTimeStyles values AssumeLocal and AssumeUniversal cannot be used together.";
        internal const string Argument_CultureIetfNotSupported =
            "Culture IETF Name {0} is not a recognized IETF name.";
        internal const string Argument_CultureIsNeutral =
            "Culture ID {0} (0x{0:X4}) is a neutral culture; a region cannot be created from it.";
        internal const string Argument_CultureNotSupported =
            "Culture is not supported.";
        internal const string Argument_CultureNotSupportedInInvariantMode =
            "Only the invariant culture is supported in globalization-invariant mode. See https://aka.ms/GlobalizationInvariantMode for more information.";
        internal const string Argument_CustomCultureCannotBePassedByNumber =
            "Customized cultures cannot be passed by LCID, only by name.";
        internal const string Argument_InvalidArrayLength =
            "Length of the array must be {0}.";
        internal const string Argument_InvalidCalendar =
            "Not a valid calendar for the given culture.";
        internal const string Argument_InvalidCultureName =
            "Culture name '{0}' is not supported.";
        internal const string Argument_InvalidDateTimeStyles =
            "An undefined DateTimeStyles value is being used.";
        internal const string Argument_InvalidDigitSubstitution =
            "The DigitSubstitution property must be of a valid member of the DigitShapes enumeration. Valid entries include Context, NativeNational or None.";
        internal const string Argument_InvalidFlag =
            "Value of flags is invalid.";
        internal const string Argument_InvalidGroupSize =
            "Every element in the value array should be between one and nine, except for the last element, which can be zero.";
        internal const string Argument_InvalidNormalizationForm =
            "Invalid or unsupported normalization form.";
        internal const string Argument_InvalidNativeDigitCount =
            "The NativeDigits array must contain exactly ten members.";
        internal const string Argument_InvalidNativeDigitValue =
            "Each member of the NativeDigits array must be a single text element (one or more UTF-16 code points) with a Unicode Nd (Number, Decimal Digit) property indicating it is a digit.";
        internal const string Argument_InvalidNumberStyles =
            "An undefined NumberStyles value is being used.";
        internal const string Argument_InvalidNeutralRegionName =
            "The region name {0} should not correspond to neutral culture; a specific culture name is required.";
        internal const string Argument_InvalidResourceCultureName =
            "The given culture name '{0}' cannot be used to locate a resource file. Resource filenames must consist of only letters, numbers, hyphens or underscores.";
        internal const string Argument_NoRegionInvariantCulture =
            "There is no region associated with the Invariant Culture (Culture ID: 0x7F).";
        internal const string Argument_OneOfCulturesNotSupported =
            "Culture name {0} or {1} is not supported.";
        internal const string Format_BadFormatSpecifier =
            "Format specifier '{0}' was invalid.";
        internal const string Format_BadQuote =
            "Cannot find a matching quote character for the character '{0}'.";
        internal const string Format_BadTimeSpan =
            "String '{0}' was not recognized as a valid TimeSpan.";
        internal const string Format_InvalidString =
            "Input string was not in a correct format.";
        internal const string Format_NoFormatSpecifier =
            "No format specifiers were provided.";
        internal const string InvalidOperation_EnumOpCantHappen =
            "Enumeration has either not started or has already finished.";
        internal const string ArgumentOutOfRange_AddValue =
            "Value to add was out of range.";
        internal const string ArgumentOutOfRange_BadHourMinuteSecond =
            "Hour, Minute, and Second parameters describe an un-representable DateTime.";
        internal const string ArgumentOutOfRange_BadYearMonthDay =
            "Year, Month, and Day parameters describe an un-representable DateTime.";
        internal const string ArgumentOutOfRange_Bounds_Lower_Upper =
            "Argument must be between {0} and {1}.";
        internal const string ArgumentOutOfRange_CalendarRange =
            "Specified time is not supported in this calendar. It should be between {0} (Gregorian date) and {1} (Gregorian date), inclusive.";
        internal const string ArgumentOutOfRange_Day =
            "Day must be between 1 and {0} for month {1}.";
        internal const string ArgumentOutOfRange_DayOfWeek =
            "The DayOfWeek enumeration must be in the range 0 through 6.";
        internal const string ArgumentOutOfRange_Era =
            "Time value was out of era range.";
        internal const string ArgumentOutOfRange_IndexCountBuffer =
            "Index and count must refer to a location within the buffer.";
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual =
            "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string ArgumentOutOfRange_InvalidEraValue =
            "Era value was not valid.";
        internal const string ArgumentOutOfRange_Month =
            "Month must be between one and twelve.";
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string ArgumentOutOfRange_Range =
            "Valid values are between {0} and {1}, inclusive.";
        internal const string ArgumentOutOfRange_Week_ISO =
            "The week parameter must be in the range 1 through 53.";
        internal const string ArgumentOutOfRange_Year =
            "Year must be between 1 and 9999.";
        internal const string Argument_IdnBadBidi =
            "Left to right characters may not be mixed with right to left characters in IDN labels.";
        internal const string Argument_IdnBadLabelSize =
            "IDN labels must be between 1 and 63 characters long.";
        internal const string Argument_IdnBadNameSize =
            "IDN names must be between 1 and {0} characters long.";
        internal const string Argument_IdnBadPunycode =
            "Invalid IDN encoded string.";
        internal const string Argument_IdnBadStd3 =
            "Label contains character '{0}' not allowed with UseStd3AsciiRules";
        internal const string Argument_IdnIllegalName =
            "Decoded string is not a valid IDN name.";
        internal const string Argument_InvalidCharSequence =
            "Invalid Unicode code point found at index {0}.";
        internal const string Argument_NoEra =
            "No Era was supplied.";
        internal const string Argument_ResultCalendarRange =
            "The result is out of the supported range for this calendar. The result should be between {0} (Gregorian date) and {1} (Gregorian date), inclusive.";
        internal const string InvalidOperation_DateTimeParsing =
            "Internal Error in DateTime and Calendar operations.";
        internal const string InvalidOperation_ReadOnly =
            "Instance is read-only.";
        internal const string Overflow_TimeSpanElementTooLarge =
            "The TimeSpan string '{0}' could not be parsed because at least one of the numeric components is out of range or contains too many digits.";

        // NetWasm strings for its globalization boundaries.

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);

        internal static string Format(IFormatProvider? provider, string format, params object?[] args) =>
            string.Format(provider, format, args);
    }
}
