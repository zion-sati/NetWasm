// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Threading.Tasks.Parallel uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Threading.Tasks.Parallel's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string ParallelState_Break_InvalidOperationException_BreakAfterStop = "Break was called after Stop was called.";
        internal const string ParallelState_NotSupportedException_UnsupportedMethod = "This method is not supported.";
        internal const string ParallelState_Stop_InvalidOperationException_StopAfterBreak = "Stop was called after Break was called.";
        internal const string Parallel_ForEach_NullEnumerator = "The source returned a null enumerator.";
        internal const string Parallel_ForEach_OrderedPartitionerKeysNotNormalized = "This method requires the use of an OrderedPartitioner with the KeysNormalized property set to true.";
        internal const string Parallel_ForEach_PartitionerNotDynamic = "The Partitioner must support dynamic partitioning.";
        internal const string Parallel_ForEach_PartitionerReturnedNull = "The Partitioner returned a null partitioner source.";
        internal const string Parallel_Invoke_ActionNull = "One of the actions was null.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
    }
}
