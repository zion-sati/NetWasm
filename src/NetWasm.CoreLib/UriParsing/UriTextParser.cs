// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriTextParser(
    IUriSchemeEndReader schemes,
    IUriAuthorityReader authorities,
    IUriPathReader paths) : IUriTextParser
{
    public UriParseResult Parse(string uriString, UriKind uriKind)
    {
        var schemeEnd = schemes.Read(uriString);
        var absolute = schemeEnd >= 0;
        if (uriKind == UriKind.Absolute && !absolute)
        {
            throw new UriFormatException(
                "A relative URI cannot be used where an absolute URI is required.");
        }
        if (uriKind == UriKind.Relative && absolute)
        {
            throw new UriFormatException(
                "An absolute URI cannot be used where a relative URI is required.");
        }

        if (!absolute)
        {
            return new UriParseResult(
                uriString,
                string.Empty,
                string.Empty,
                string.Empty,
                uriString,
                string.Empty,
                string.Empty,
                -1,
                false,
                uriString.IndexOf('%') >= 0,
                uriString);
        }

        var scheme = uriString.Substring(0, schemeEnd).ToLowerInvariant();
        var cursor = schemeEnd + 1;
        var authorityEnd = cursor;
        var hasAuthority = cursor + 1 < uriString.Length &&
            uriString[cursor] == '/' && uriString[cursor + 1] == '/';
        if (hasAuthority)
        {
            cursor += 2;
            authorityEnd = FindFirst(uriString, cursor, '/', '?', '#');
            if (authorityEnd < 0)
            {
                authorityEnd = uriString.Length;
            }
        }

        var authority = hasAuthority
            ? uriString.Substring(cursor, authorityEnd - cursor)
            : string.Empty;
        var authorityParts = authorities.Read(authority);
        var rawPathParts = paths.Read(uriString.Substring(authorityEnd));
        var path = NormalizePath(rawPathParts.Path, hasAuthority);
        var query = NormalizeComponent(rawPathParts.Query);
        var fragment = NormalizeComponent(rawPathParts.Fragment);
        var userInfo = authorityParts.UserInfo;
        var host = NormalizeHost(authorityParts.Host);
        if (!hasAuthority && scheme == "mailto")
        {
            var at = path.LastIndexOf('@');
            if (at > 0 && at < path.Length - 1)
            {
                userInfo = path.Substring(0, at);
                host = NormalizeHost(path.Substring(at + 1));
                path = string.Empty;
            }
        }
        var canonicalAuthority = userInfo.Length == 0
            ? host
            : userInfo + "@" + host;
        var defaultPort = GetDefaultPort(scheme);
        if (authorityParts.Port >= 0 && authorityParts.Port != defaultPort)
        {
            canonicalAuthority += ":" + authorityParts.Port;
        }
        var opaquePath = scheme == "mailto" && host.Length > 0
            ? userInfo + "@" + host
            : path;
        var canonical = hasAuthority
            ? scheme + "://" + canonicalAuthority +
                (path.Length == 0 ? "/" : path) +
                (query.Length == 0 ? string.Empty : "?" + query) +
                (fragment.Length == 0 ? string.Empty : "#" + fragment)
            : scheme + ":" + opaquePath +
                (query.Length == 0 ? string.Empty : "?" + query) +
                (fragment.Length == 0 ? string.Empty : "#" + fragment);
        return new UriParseResult(
            uriString,
            scheme,
            userInfo,
            host,
            path,
            query,
            fragment,
            authorityParts.Port,
            true,
            uriString.IndexOf('%') >= 0,
            canonical);
    }

    private static string NormalizeHost(string value) =>
        value.Length == 0 || (value[0] == '[' && value[value.Length - 1] == ']')
            ? value.ToLowerInvariant()
            : value.ToLowerInvariant();

    private static int GetDefaultPort(string scheme) => scheme switch
    {
        "http" or "ws" => 80,
        "https" or "wss" => 443,
        "ftp" => 21,
        "gopher" => 70,
        "mailto" => 25,
        "nntp" => 119,
        "ssh" => 22,
        "telnet" => 23,
        _ => -1,
    };

    private static string NormalizeComponent(string value) =>
        value.Replace(" ", "%20", StringComparison.Ordinal);

    private static string NormalizePath(string value, bool hasAuthority)
    {
        if (value.Length == 0)
        {
            return hasAuthority ? "/" : string.Empty;
        }

        var escaped = value.Replace(" ", "%20", StringComparison.Ordinal);
        if (escaped.IndexOf("/./", StringComparison.Ordinal) < 0 &&
            escaped.IndexOf("/../", StringComparison.Ordinal) < 0 &&
            !escaped.EndsWith("/.", StringComparison.Ordinal) &&
            !escaped.EndsWith("/..", StringComparison.Ordinal))
        {
            return escaped;
        }

        var absolute = escaped[0] == '/';
        var trailing = escaped.EndsWith("/", StringComparison.Ordinal) ||
            escaped.EndsWith("/.", StringComparison.Ordinal) ||
            escaped.EndsWith("/..", StringComparison.Ordinal);
        var segments = new string[escaped.Length + 1];
        var count = 0;
        var start = absolute ? 1 : 0;
        while (start <= escaped.Length)
        {
            var end = escaped.IndexOf('/', start);
            if (end < 0)
            {
                end = escaped.Length;
            }
            var segment = escaped.Substring(start, end - start);
            if (segment == "..")
            {
                if (count > 0 && segments[count - 1] != "..")
                {
                    count--;
                }
                else if (!absolute)
                {
                    segments[count++] = segment;
                }
            }
            else if (segment.Length > 0 && segment != ".")
            {
                segments[count++] = segment;
            }
            if (end == escaped.Length)
            {
                break;
            }
            start = end + 1;
        }

        var result = absolute ? "/" : string.Empty;
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                result += "/";
            }
            result += segments[index];
        }
        if (trailing && !result.EndsWith("/", StringComparison.Ordinal))
        {
            result += "/";
        }
        return result.Length == 0 && hasAuthority ? "/" : result;
    }

    private static int FindFirst(string value, int start, params char[] delimiters)
    {
        for (var index = start; index < value.Length; index++)
        {
            for (var delimiter = 0; delimiter < delimiters.Length; delimiter++)
            {
                if (value[index] == delimiters[delimiter])
                {
                    return index;
                }
            }
        }
        return -1;
    }
}
