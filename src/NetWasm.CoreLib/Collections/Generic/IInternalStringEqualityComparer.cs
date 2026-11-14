// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Collections.Generic
{
    internal interface IInternalStringEqualityComparer : IEqualityComparer<string?>
    {
        IEqualityComparer<string?> GetUnderlyingEqualityComparer();

        internal static IEqualityComparer<string?> GetUnderlyingEqualityComparer(
            IEqualityComparer<string?> outerComparer) =>
            outerComparer is IInternalStringEqualityComparer internalComparer
                ? internalComparer.GetUnderlyingEqualityComparer()
                : outerComparer;
    }
}
