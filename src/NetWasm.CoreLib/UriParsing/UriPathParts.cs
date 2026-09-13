// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal readonly struct UriPathParts
{
    public UriPathParts(string path, string query, string fragment)
    {
        Path = path;
        Query = query;
        Fragment = fragment;
    }

    public string Path { get; }
    public string Query { get; }
    public string Fragment { get; }
}
