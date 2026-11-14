// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Security.Cryptography
{
    internal static class SR
    {
        internal const string Arg_RankMultiDimNotSupported =
            "Only single dimensional arrays are supported for the requested action.";
        internal const string ArgumentOutOfRange_IndexMustBeLess =
            "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string Arg_EnumIllegalVal =
            "Illegal enum value: {0}.";
        internal const string Argument_DestinationTooShort =
            "Destination is too short.";
        internal const string Argument_InvalidOffLen =
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        internal const string Argument_InvalidValue =
            "Value was invalid.";
        internal const string Argument_StreamNotReadable =
            "Stream was not readable.";
        internal const string Argument_StreamNotWritable =
            "Stream was not writable.";
        internal const string NotSupported_UnreadableStream =
            "Stream does not support reading.";
        internal const string NotSupported_UnseekableStream =
            "Stream does not support seeking.";
        internal const string NotSupported_UnwritableStream =
            "Stream does not support writing.";


        internal const string Argument_InvalidFlag =
            "Value of flags is invalid.";
        internal const string InvalidOperation_EnumNotStarted =
            "Enumeration has not started. Call MoveNext.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
