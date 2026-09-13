// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Immutable collection resource strings are adapted from the pinned
// System.Collections.Immutable resource set at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. NetWasm keeps deterministic
// invariant messages and does not deploy satellite resource assemblies.

namespace System.Collections.Immutable
{
    internal static class SR
    {
        internal const string ArrayInitializedStateNotEqual =
            "The two arrays have different initialized states.";
        internal const string ArrayLengthsNotEqual =
            "The two arrays have different lengths.";
        internal const string CapacityMustBeGreaterThanOrEqualToCount =
            "Capacity must be greater than or equal to Count.";
        internal const string CapacityMustEqualCountOnMove =
            "Capacity must equal Count when moving the builder contents.";
        internal const string CannotFindOldValue =
            "The old value could not be found.";
        internal const string CollectionModifiedDuringEnumeration =
            "Collection was modified; enumeration operation may not execute.";
        internal const string InvalidOperationOnDefaultArray =
            "This operation cannot be performed on a default ImmutableArray.";
    }
}
