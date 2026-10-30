// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.

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
