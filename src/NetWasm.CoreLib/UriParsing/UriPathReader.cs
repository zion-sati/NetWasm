// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriPathReader : IUriPathReader
{
    public UriPathParts Read(string value)
    {
        var fragmentStart = value.IndexOf('#');
        var fragment = string.Empty;
        var withoutFragment = value;
        if (fragmentStart >= 0)
        {
            fragment = value.Substring(fragmentStart + 1);
            withoutFragment = value.Substring(0, fragmentStart);
        }

        var queryStart = withoutFragment.IndexOf('?');
        if (queryStart >= 0)
        {
            return new UriPathParts(
                withoutFragment.Substring(0, queryStart),
                withoutFragment.Substring(queryStart + 1),
                fragment);
        }
        return new UriPathParts(withoutFragment, string.Empty, fragment);
    }
}
