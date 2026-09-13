// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal readonly struct UriParseResult
{
    public UriParseResult(
        string originalString,
        string scheme,
        string userInfo,
        string host,
        string path,
        string query,
        string fragment,
        int port,
        bool isAbsolute,
        bool userEscaped,
        string? canonicalString = null)
    {
        OriginalString = originalString;
        Scheme = scheme;
        UserInfo = userInfo;
        Host = host;
        Path = path;
        Query = query;
        Fragment = fragment;
        Port = port;
        IsAbsolute = isAbsolute;
        UserEscaped = userEscaped;
        CanonicalString = canonicalString ?? originalString;
    }

    public string OriginalString { get; }
    public string Scheme { get; }
    public string UserInfo { get; }
    public string Host { get; }
    public string Path { get; }
    public string Query { get; }
    public string Fragment { get; }
    public int Port { get; }
    public bool IsAbsolute { get; }
    public bool UserEscaped { get; }
    public string CanonicalString { get; }
}
