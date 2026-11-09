// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Security
{
    internal static class SR
    {
        internal const string ArgumentNull_Child =
            "Cannot have a null child.";
        internal const string Argument_AttributeNamesMustBeUnique =
            "Attribute names must be unique.";
        internal const string Argument_InvalidElementName =
            "Invalid element name '{0}'.";
        internal const string Argument_InvalidElementTag =
            "Invalid element tag '{0}'.";
        internal const string Argument_InvalidElementText =
            "Invalid element text '{0}'.";
        internal const string Argument_InvalidElementValue =
            "Invalid element value '{0}'.";
        internal const string ArgumentOutOfRange_Capacity =
            "Capacity exceeds maximum capacity.";
        internal const string ArgumentOutOfRange_IndexString =
            "Index was out of range. Must be non-negative and less than the length of the string.";
        internal const string InvalidOperation_ReadOnly =
            "Instance is read-only.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
