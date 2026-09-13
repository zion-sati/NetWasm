// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriHostClassifier : IUriHostClassifier
{
    public UriHostNameType Classify(string name)
    {
        if (name.Length == 0)
        {
            return UriHostNameType.Unknown;
        }
        if (LooksLikeIpv6(name))
        {
            return UriHostNameType.IPv6;
        }

        var parts = 0;
        var partLength = 0;
        var partValue = 0;
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '.')
            {
                if (partLength == 0 || partValue > 255)
                {
                    return UriHostNameType.Unknown;
                }
                parts++;
                partLength = 0;
                partValue = 0;
                continue;
            }
            if (character is < '0' or > '9')
            {
                return IsDnsName(name) ? UriHostNameType.Dns : UriHostNameType.Unknown;
            }
            partLength++;
            partValue = (partValue * 10) + character - '0';
        }
        if (partLength == 0 || partValue > 255)
        {
            return IsDnsName(name) ? UriHostNameType.Dns : UriHostNameType.Unknown;
        }
        parts++;
        return parts <= 4 ? UriHostNameType.IPv4 : UriHostNameType.Unknown;
    }

    private static bool IsDnsName(string name)
    {
        if (name.Length == 0 || name[0] == '.' || name[name.Length - 1] == '.')
        {
            return false;
        }
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and
                not (>= '0' and <= '9') and not '-' and not '.')
            {
                return false;
            }
        }
        return true;
    }

    private static bool LooksLikeIpv6(string name)
    {
        var start = name[0] == '[' ? 1 : 0;
        var end = name.Length - (name[name.Length - 1] == ']' ? 1 : 0);
        if (end - start < 2 || name.IndexOf(':', start) < 0)
        {
            return false;
        }
        for (var index = start; index < end; index++)
        {
            var character = name[index];
            if (character != ':' && character != '.' &&
                !(character is >= '0' and <= '9') &&
                !(character is >= 'a' and <= 'f') &&
                !(character is >= 'A' and <= 'F'))
            {
                return false;
            }
        }
        return true;
    }
}
