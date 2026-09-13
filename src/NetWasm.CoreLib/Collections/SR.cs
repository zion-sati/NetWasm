// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections
{
    internal static class SR
    {
        internal const string Arg_ArrayPlusOffTooSmall =
            "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
        internal const string Arg_KeyNotFoundWithKey =
            "The given key '{0}' was not present in the dictionary.";
        internal const string Arg_NonZeroLowerBound =
            "The lower bound of target array must be zero.";
        internal const string Arg_RankMultiDimNotSupported =
            "Only single dimensional arrays are supported for the requested action.";
        internal const string Argument_IncompatibleArrayType =
            "Target array type is not compatible with the type of items in the collection.";
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string CapacityMustBeGreaterThanOrEqualToCount =
            "Capacity was less than the current Count of elements.";
        internal const string InvalidOperation_IncompatibleComparer =
            "The collection, in conjunction with its comparer, does not support the specified alternate key type.";

        internal static string Format(string format, object? value) =>
            format.Replace("{0}", value?.ToString() ?? string.Empty);
    }
}
