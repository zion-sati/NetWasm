// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriSegmentReader : IUriSegmentReader
{
    public string[] Read(string path, bool isAbsolute)
    {
        if (!isAbsolute || path.Length == 0)
        {
            return Array.Empty<string>();
        }

        var count = 0;
        for (var index = 0; index < path.Length; index++)
        {
            if (path[index] == '/')
            {
                count++;
            }
        }

        var result = new string[count +
            (path.EndsWith("/", StringComparison.Ordinal) ? 0 : 1)];
        var start = 0;
        var slot = 0;
        for (var index = 0; index < path.Length; index++)
        {
            if (path[index] != '/')
            {
                continue;
            }
            result[slot++] = path.Substring(start, index - start + 1);
            start = index + 1;
        }
        if (start < path.Length)
        {
            result[slot] = path.Substring(start);
        }
        return result;
    }
}
