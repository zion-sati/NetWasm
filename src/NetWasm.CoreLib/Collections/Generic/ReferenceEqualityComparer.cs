// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib
// ReferenceEqualityComparer.cs at commit 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    public sealed class ReferenceEqualityComparer :
        IEqualityComparer<object?>, System.Collections.IEqualityComparer
    {
        private ReferenceEqualityComparer()
        {
        }

        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);

        // RuntimeHelpers.GetHashCode is not yet an intrinsic in NetWasm's
        // object ABI. Object.GetHashCode is the stable available fallback.
        public int GetHashCode(object? value) => value?.GetHashCode() ?? 0;
    }
}
