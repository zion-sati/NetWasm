// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Runtime.Numerics uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Runtime.Numerics's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string Argument_InvalidHexBinaryStyle = "With the AllowHexSpecifier or AllowBinarySpecifier bit set in the enum bit field, the only other valid bits that can be combined into the enum value must be AllowLeadingWhite and AllowTrailingWhite.";
        internal const string Argument_InvalidNumberStyles = "An undefined NumberStyles value is being used.";
        internal const string Format_TooLarge = "The value is too large to be represented by this format specifier.";
        internal const string Overflow_ParseBigInteger = "The value could not be parsed.";
        internal const string Argument_MinMaxValue = "'{0}' cannot be greater than {1}.";
        internal const string Argument_MustBeBigInt = "The parameter must be a BigInteger.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string Overflow_BigIntInfinity = "BigInteger cannot represent infinity.";
        internal const string Overflow_Int128 = "Value was either too large or too small for an Int128.";
        internal const string Overflow_Int32 = "Value was either too large or too small for an Int32.";
        internal const string Overflow_Int64 = "Value was either too large or too small for an Int64.";
        internal const string Overflow_Negative_Unsigned = "Negative values do not have an unsigned representation.";
        internal const string Overflow_NotANumber = "The value is not a number.";
        internal const string Overflow_UInt128 = "Value was either too large or too small for a UInt128.";
        internal const string Overflow_UInt32 = "Value was either too large or too small for a UInt32.";
        internal const string Overflow_UInt64 = "Value was either too large or too small for a UInt64.";
        internal const string ArgumentOutOfRange_NeedNonNegNum = "Non-negative number required.";
        internal const string Argument_BadFormatSpecifier = "Format specifier was invalid.";
        internal const string Arg_HexStyleNotSupported = "The number style AllowHexSpecifier is not supported on floating point data types.";
    }
}
