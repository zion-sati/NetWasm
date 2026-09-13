// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriEqualityComparer : IUriEqualityComparer
{
    public bool AreEqual(Uri left, Uri right) =>
        string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
}
