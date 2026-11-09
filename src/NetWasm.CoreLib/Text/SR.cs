// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Text
{
    internal static class SR
    {
        internal const string ArgumentOutOfRange_GetByteCountOverflow =
            "Too many characters. The resulting number of bytes is larger than what can be returned as an int.";
        internal const string ArgumentOutOfRange_IndexCount =
            "Index and count must refer to a location within the string.";
        internal const string ArgumentOutOfRange_IndexCountBuffer =
            "Index and count must refer to a location within the buffer.";
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual =
            "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string Argument_EncodingConversionOverflowBytes =
            "The output byte buffer is too small to contain the encoded data, encoding codepage '{0}' and fallback '{1}'.";
        internal const string Argument_EncodingConversionOverflowChars =
            "The output char buffer is too small to contain the decoded characters, encoding codepage '{0}' and fallback '{1}'.";
        internal const string Argument_InvalidCharSequenceNoIndex =
            "String contains invalid Unicode code points.";

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);
    }
}
