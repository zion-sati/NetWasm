// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Linq.Parallel uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Linq.Parallel's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string Arg_HTCapacityOverflow = "Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table.";
        internal const string MoreThanOneMatch = "Sequence contains more than one matching element";
        internal const string NoElements = "Sequence contains no elements";
        internal const string PLINQ_CommonEnumerator_Current_NotStarted = "Enumeration has not started. MoveNext must be called to initiate enumeration.";
        internal const string PLINQ_DisposeRequested = "The query enumerator has been disposed.";
        internal const string PLINQ_EnumerationPreviouslyFailed = "The query enumerator previously threw an exception.";
        internal const string PLINQ_ExternalCancellationRequested = "The query has been canceled via the token supplied to WithCancellation.";
        internal const string ParallelEnumerable_BinaryOpMustUseAsParallel = "The second data source of a binary operator must be of type System.Linq.ParallelQuery<T> rather than System.Collections.Generic.IEnumerable<T>. To fix this problem, use the AsParallel() extension method to convert the right data source to System.Linq.ParallelQuery<T>.";
        internal const string ParallelEnumerable_WithMergeOptions_InvalidOptions = "The mergeOptions argument contains an invalid value.";
        internal const string ParallelEnumerable_WithQueryExecutionMode_InvalidMode = "The executionMode argument contains an invalid value.";
        internal const string ParallelPartitionable_IncorretElementCount = "The returned array's length must equal the number of partitions requested.";
        internal const string ParallelPartitionable_NullElement = "Elements returned must not be null.";
        internal const string ParallelPartitionable_NullReturn = "The return value must not be null.";
        internal const string ParallelQuery_DuplicateDOP = "The WithDegreeOfParallelism operator may be used at most once in a query.";
        internal const string ParallelQuery_DuplicateExecutionMode = "The WithExecutionMode operator may be used at most once in a query.";
        internal const string ParallelQuery_DuplicateMergeOptions = "The WithMergeOptions operator may be used at most once in a query.";
        internal const string ParallelQuery_DuplicateTaskScheduler = "The WithTaskScheduler operator may be used at most once in a query.";
        internal const string ParallelQuery_DuplicateWithCancellation = "The WithCancellation operator may by used at most once in a query.";
        internal const string ParallelQuery_InvalidAsOrderedCall = "AsOrdered may only be called on the result of AsParallel, ParallelEnumerable.Range, or ParallelEnumerable.Repeat.";
        internal const string ParallelQuery_InvalidNonGenericAsOrderedCall = "Non-generic AsOrdered may only be called on the result of the non-generic AsParallel.";
        internal const string ParallelQuery_PartitionerNotOrderable = "AsOrdered may not be used with a partitioner that is not orderable.";
        internal const string PartitionerQueryOperator_NullPartition = "Partitioner returned a null partition.";
        internal const string PartitionerQueryOperator_NullPartitionList = "Partitioner returned null instead of a list of partitions.";
        internal const string PartitionerQueryOperator_WrongNumberOfPartitions = "Partitioner returned a wrong number of partitions.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
    }
}
