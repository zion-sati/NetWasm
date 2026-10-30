// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 474ed963699fc29809015392738a15231aaae2de.

namespace System.Collections
{
    internal static class SR
    {
        internal const string Arg_ArrayPlusOffTooSmall =
            "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
        internal const string Arg_ArrayLengthsDiffer =
            "Array lengths must be the same.";
        internal const string Arg_BitArrayTypeUnsupported =
            "Only supported array types for CopyTo on BitArrays are Boolean[], Int32[] and Byte[].";
        internal const string Arg_BogusIComparer =
            "Unable to sort because the IComparer.Compare() method returns inconsistent results. Either a value does not compare equal to itself, or one value repeatedly compared to another value yields different results. IComparer: '{0}'.";
        internal const string Arg_KeyNotFoundWithKey =
            "The given key '{0}' was not present in the dictionary.";
        internal const string Arg_InsufficientSpace =
            "Insufficient space in the target location to copy the information.";
        internal const string Arg_NonZeroLowerBound =
            "The lower bound of target array must be zero.";
        internal const string Arg_RankMultiDimNotSupported =
            "Only single dimensional arrays are supported for the requested action.";
        internal const string Arg_WrongType =
            "The value '{0}' is not of type '{1}' and cannot be used in this generic collection.";
        internal const string Arg_HTCapacityOverflow =
            "Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table.";
        internal const string Arg_CannotMixComparisonInfrastructure =
            "The usage of IKeyComparer and IHashCodeProvider/IComparer interfaces cannot be mixed; use one or the other.";
        internal const string Argument_AddingDuplicate =
            "An item with the same key has already been added. Key: {0}";
        internal const string Argument_AddingDuplicate__ =
            "Item has already been added. Key in dictionary: '{0}'  Key being added: '{1}'";
        internal const string Argument_ArrayTooLarge =
            "The input array length must not exceed Int32.MaxValue / {0}. Otherwise BitArray.Length would exceed Int32.MaxValue.";
        internal const string Argument_IncompatibleArrayType =
            "Target array type is not compatible with the type of items in the collection.";
        internal const string Argument_InvalidOffLen =
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        internal const string Argument_ImplementIComparable =
            "At least one object must implement IComparable.";
        internal const string ArgumentOutOfRange_MustBeNonNegNum =
            "'{0}' must be non-negative.";
        internal const string ArgumentOutOfRange_BiggerThanCollection =
            "Larger than collection size.";
        internal const string ArgumentOutOfRange_Count =
            "Count must be positive and count must refer to a location within the string/array/collection.";
        internal const string ArgumentOutOfRange_IndexMustBeLess =
            "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual =
            "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string ArgumentOutOfRange_HashtableLoadFactor =
            "Load factor needs to be between 0.1 and 1.0.";
        internal const string ArgumentOutOfRange_ListInsert =
            "Index must be within the bounds of the List.";
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string ArgumentOutOfRange_SmallCapacity =
            "capacity was less than the current size.";
        internal const string CapacityMustBeGreaterThanOrEqualToCount =
            "Capacity was less than the current Count of elements.";
        internal const string ConcurrentCollection_SyncRoot_NotSupported =
            "The SyncRoot property may not be used for the synchronization of concurrent collections.";
        internal const string ConcurrentDictionary_ArrayIncorrectType =
            "The array is multidimensional, or the type parameter for the set cannot be cast automatically to the type of the destination array.";
        internal const string ConcurrentDictionary_ArrayNotLargeEnough =
            "The index is equal to or greater than the length of the array, or the number of elements in the dictionary is greater than the available space from index to the end of the destination array.";
        internal const string ConcurrentDictionary_ConcurrencyLevelMustBePositiveOrNegativeOne =
            "The concurrencyLevel argument must be positive, or -1 to indicate a default level.";
        internal const string ConcurrentDictionary_KeyAlreadyExisted =
            "The key already existed in the dictionary.";
        internal const string ConcurrentDictionary_SourceContainsDuplicateKeys =
            "The source argument contains duplicate keys.";
        internal const string ConcurrentDictionary_TypeOfKeyIncorrect =
            "The key was of an incorrect type for this dictionary.";
        internal const string ConcurrentDictionary_TypeOfValueIncorrect =
            "The value was of an incorrect type for this dictionary.";
        internal const string InvalidOperation_IncompatibleComparer =
            "The collection, in conjunction with its comparer, does not support the specified alternate key type.";
        internal const string InvalidOperation_ConcurrentOperationsNotSupported =
            "Concurrent operations are not supported.";
        internal const string InvalidOperation_IComparerFailed =
            "Failed to compare two elements in the array.";
        internal const string InvalidOperation_HashInsertFailed =
            "Hashtable insert failed. Load factor too high. The most common cause is multiple threads writing to the Hashtable simultaneously.";
        internal const string InvalidOperation_EmptyQueue =
            "Queue empty.";
        internal const string InvalidOperation_EmptyStack =
            "Stack empty.";
        internal const string ExternalLinkedListNode =
            "The LinkedList node does not belong to current LinkedList.";
        internal const string InvalidOperation_EnumFailedVersion =
            "Collection was modified after the enumerator was instantiated.";
        internal const string InvalidOperation_EnumEnded =
            "Enumeration already finished.";
        internal const string InvalidOperation_EnumNotStarted =
            "Enumeration has not started. Call MoveNext.";
        internal const string InvalidOperation_UnderlyingArrayListChanged =
            "This range in the underlying list is invalid. A possible cause is that elements were removed.";
        internal const string InvalidOperation_EnumOpCantHappen =
            "Enumeration has either not started or has already finished.";
        internal const string LinkedListEmpty =
            "The LinkedList is empty.";
        internal const string LinkedListNodeIsAttached =
            "The LinkedList node already belongs to a LinkedList.";
        internal const string NotSupported_KeyCollectionSet =
            "Mutating a key collection derived from a dictionary is not allowed.";
        internal const string NotSupported_FixedSizeCollection =
            "Collection was of a fixed size.";
        internal const string NotSupported_RangeCollection =
            "The specified operation is not supported on Ranges.";
        internal const string NotSupported_ReadOnlyCollection =
            "Collection is read-only.";
        internal const string NotSupported_SortedListNestedWrite =
            "This operation is not supported on SortedList nested types because they require modifying the original SortedList.";
        internal const string NotSupported_ValueCollectionSet =
            "Mutating a value collection derived from a dictionary is not allowed.";
        internal const string SortedSet_LowerValueGreaterThanUpperValue =
            "Must be less than or equal to upperValue.";

        internal static string Format(string format, object? value) =>
            format.Replace("{0}", value?.ToString() ?? string.Empty);

        internal static string Format(string format, object? first, object? second) =>
            format.Replace("{0}", first?.ToString() ?? string.Empty)
                .Replace("{1}", second?.ToString() ?? string.Empty);
    }
}
