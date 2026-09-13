// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriAuthorityFormatter : IUriAuthorityFormatter
{
    public string Format(string userInfo, string host, int port, bool includePort)
    {
        var authority = userInfo.Length == 0 ? string.Empty : userInfo + "@";
        authority += host;
        if (includePort && port >= 0)
        {
            authority += ":" + port;
        }
        return authority;
    }
}
