// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Collections uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Collections's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string Arg_InsufficientSpace = "Insufficient space in the target location to copy the information.";
        internal const string Arg_NonZeroLowerBound = "The lower bound of target array must be zero.";
        internal const string Arg_RankMultiDimNotSupported = "Only single dimensional arrays are supported for the requested action.";
        internal const string ArgumentOutOfRange_BiggerThanCollection = "Must be less than or equal to the size of the collection.";
        internal const string Argument_IncompatibleArrayType = "Target array type is not compatible with the type of items in the collection.";
        internal const string ExternalLinkedListNode = "The LinkedList node does not belong to current LinkedList.";
        internal const string InvalidOperation_EnumFailedVersion = "Collection was modified after the enumerator was instantiated.";
        internal const string InvalidOperation_EnumOpCantHappen = "Enumeration has either not started or has already finished.";
        internal const string LinkedListEmpty = "The LinkedList is empty.";
        internal const string LinkedListNodeIsAttached = "The LinkedList node already belongs to a LinkedList.";
        internal const string Serialization_MissingValues = "The values for this dictionary are missing.";
        internal const string Arg_ArrayPlusOffTooSmall = "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
        internal const string Arg_WrongType = "The value '{0}' is not of type '{1}' and cannot be used in this generic collection.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual = "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string Argument_InvalidOffLen = "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        internal const string InvalidOperation_EmptyQueue = "Queue empty.";
        internal const string Arg_KeyNotFoundWithKey = "The given key '{0}' was not present in the dictionary.";
        internal const string Argument_AddingDuplicate = "An item with the same key has already been added. Key: {0}";
        internal const string NotSupported_KeyCollectionSet = "Mutating a key collection derived from a dictionary is not allowed.";
        internal const string NotSupported_ValueCollectionSet = "Mutating a value collection derived from a dictionary is not allowed.";
        internal const string ArgumentOutOfRange_IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string ArgumentOutOfRange_SmallCapacity = "capacity was less than the current size.";
        internal const string NotSupported_SortedListNestedWrite = "This operation is not supported on SortedList nested types because they require modifying the original SortedList.";
        internal const string Serialization_InvalidOnDeser = "OnDeserialization method was called while the object was not being deserialized.";
        internal const string Serialization_MismatchedCount = "The serialized Count information doesn't match the number of items.";
        internal const string SortedSet_LowerValueGreaterThanUpperValue = "Must be less than or equal to upperValue.";
        internal const string InvalidOperation_EmptyStack = "Stack empty.";
        internal const string InvalidOperation_ConcurrentOperationsNotSupported = "Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.";
        internal const string Arg_HTCapacityOverflow = "Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table.";
    }
}
