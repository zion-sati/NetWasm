// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Collections.Immutable uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Collections.Immutable's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string Arg_ArrayPlusOffTooSmall = "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
        internal const string Arg_NonZeroLowerBound = "The lower bound of target array must be zero.";
        internal const string Arg_RankMultiDimNotSupported = "Only single dimensional arrays are supported for the requested action.";
        internal const string ArgumentOutOfRange_NeedNonNegNum = "Non-negative number required.";
        internal const string Argument_IncompatibleArrayType = "Target array type is not compatible with the type of items in the collection.";
        internal const string InvalidOperationOnDefaultArray = "This operation cannot be performed on a default instance of ImmutableArray<T>.  Consider initializing the array, or checking the ImmutableArray<T>.IsDefault property.";
        internal const string CapacityMustBeGreaterThanOrEqualToCount = "Capacity was less than the current Count of elements.";
        internal const string CapacityMustEqualCountOnMove = "MoveToImmutable can only be performed when Count equals Capacity.";
        internal const string ArrayInitializedStateNotEqual = "Object is not an array with the same initialization state as the array to compare it to.";
        internal const string ArrayLengthsNotEqual = "Object is not an array with the same number of elements as the array to compare it to.";
        internal const string CannotFindOldValue = "Cannot find the old value";
        internal const string CollectionModifiedDuringEnumeration = "Collection was modified; enumeration operation may not execute.";
        internal const string DuplicateKey = "An element with the same key but a different value already exists. Key: '{0}'";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string InvalidEmptyOperation = "This operation does not apply to an empty instance.";
        internal const string Arg_KeyNotFoundWithKey = "The given key '{0}' was not present in the dictionary.";
        internal const string InvalidOperation_IncompatibleComparer = "The collection, in conjunction with its comparer, does not support the specified alternate key type.";
        internal const string Arg_HTCapacityOverflow = "Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table.";
    }
}
