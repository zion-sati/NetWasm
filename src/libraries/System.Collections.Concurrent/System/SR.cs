// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Collections.Concurrent uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Collections.Concurrent's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string BlockingCollection_Add_ConcurrentCompleteAdd = "CompleteAdding may not be used concurrently with additions to the collection.";
        internal const string BlockingCollection_Add_Failed = "The underlying collection didn't accept the item.";
        internal const string BlockingCollection_CantAddAnyWhenCompleted = "At least one of the specified collections is marked as complete with regards to additions.";
        internal const string BlockingCollection_CantTakeAnyWhenAllDone = "All collections are marked as complete with regards to additions.";
        internal const string BlockingCollection_CantTakeWhenDone = "The collection argument is empty and has been marked as complete with regards to additions.";
        internal const string BlockingCollection_Completed = "The collection has been marked as complete with regards to additions.";
        internal const string BlockingCollection_CopyTo_IncorrectType = "The array argument is of the incorrect type.";
        internal const string BlockingCollection_CopyTo_MultiDim = "The array argument is multidimensional.";
        internal const string BlockingCollection_CopyTo_NonNegative = "The index argument must be greater than or equal zero.";
        internal const string BlockingCollection_Take_CollectionModified = "The underlying collection was modified from outside of the BlockingCollection<T>.";
        internal const string BlockingCollection_TimeoutInvalid = "The specified timeout must represent a value between -1 and {0}, inclusive.";
        internal const string BlockingCollection_ValidateCollectionsArray_DispElems = "The collections argument contains at least one disposed element.";
        internal const string BlockingCollection_ValidateCollectionsArray_LargeSize = "The collections length is greater than the supported range.";
        internal const string BlockingCollection_ValidateCollectionsArray_NullElems = "The collections argument contains at least one null element.";
        internal const string BlockingCollection_ValidateCollectionsArray_ZeroSize = "The collections argument is a zero-length array.";
        internal const string BlockingCollection_ctor_CountMoreThanCapacity = "The collection argument contains more items than are allowed by the boundedCapacity.";
        internal const string Collection_CopyTo_TooManyElems = "The number of elements in the collection is greater than the available space from index to the end of the destination array.";
        internal const string ConcurrentCollection_SyncRoot_NotSupported = "The SyncRoot property may not be used for the synchronization of concurrent collections.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string ConcurrentStack_PushPopRange_InvalidCount = "The sum of the startIndex and count arguments must be less than or equal to the collection's Count.";
        internal const string Partitioner_DynamicPartitionsNotSupported = "Dynamic partitions are not supported by this partitioner.";
        internal const string PartitionerStatic_CurrentCalledBeforeMoveNext = "MoveNext must be called at least once before calling Current.";
    }
}
