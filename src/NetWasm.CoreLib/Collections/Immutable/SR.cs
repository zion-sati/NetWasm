// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Immutable collection resource strings are adapted from the pinned
// System.Collections.Immutable resource set at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce. NetWasm keeps deterministic
// invariant messages and does not deploy satellite resource assemblies.

namespace System.Collections.Immutable
{
    internal static class SR
    {
        internal const string CapacityMustBeGreaterThanOrEqualToCount =
            "Capacity was less than the current Count of elements.";

        internal static string Format(string resourceFormat, object? p1) =>
            string.Format(resourceFormat, p1);
    }
}
