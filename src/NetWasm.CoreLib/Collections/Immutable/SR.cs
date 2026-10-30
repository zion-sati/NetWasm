// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Immutable collection resource strings are adapted from the pinned
// System.Collections.Immutable resource set at commit
// 474ed963699fc29809015392738a15231aaae2de. NetWasm keeps deterministic
// invariant messages and does not deploy satellite resource assemblies.

namespace System.Collections.Immutable
{
    internal static class SR
    {
        internal const string ArrayInitializedStateNotEqual =
            "Object is not an array with the same initialization state as the array to compare it to.";
        internal const string ArrayLengthsNotEqual =
            "Object is not an array with the same number of elements as the array to compare it to.";
        internal const string CapacityMustBeGreaterThanOrEqualToCount =
            "Capacity was less than the current Count of elements.";
        internal const string CapacityMustEqualCountOnMove =
            "MoveToImmutable can only be performed when Count equals Capacity.";
        internal const string CannotFindOldValue =
            "Cannot find the old value";
        internal const string CollectionModifiedDuringEnumeration =
            "Collection was modified; enumeration operation may not execute.";
        internal const string DuplicateKey =
            "An element with the same key but a different value already exists. Key: '{0}'";
        internal const string InvalidEmptyOperation =
            "This operation does not apply to an empty instance.";
        internal const string InvalidOperationOnDefaultArray =
            "This operation cannot be performed on a default instance of ImmutableArray<T>.  Consider initializing the array, or checking the ImmutableArray<T>.IsDefault property.";

        internal static string Format(string resourceFormat, object? p1) =>
            string.Format(resourceFormat, p1);
    }
}
