// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class DefaultUriPortResolver : IDefaultUriPortResolver
{
    public int Resolve(string scheme) => scheme switch
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
}
