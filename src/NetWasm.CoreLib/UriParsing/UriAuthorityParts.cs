// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal readonly struct UriAuthorityParts
{
    public UriAuthorityParts(string userInfo, string host, int port)
    {
        UserInfo = userInfo;
        Host = host;
        Port = port;
    }

    public string UserInfo { get; }
    public string Host { get; }
    public int Port { get; }
}
