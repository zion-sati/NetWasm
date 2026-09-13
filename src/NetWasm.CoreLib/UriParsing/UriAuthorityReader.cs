// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriAuthorityReader(IUriPortReader ports) : IUriAuthorityReader
{
    public UriAuthorityParts Read(string authority)
    {
        var userInfo = string.Empty;
        var hostAndPort = authority;
        var at = hostAndPort.LastIndexOf('@');
        if (at >= 0)
        {
            userInfo = hostAndPort.Substring(0, at);
            hostAndPort = hostAndPort.Substring(at + 1);
        }

        var port = -1;
        var host = hostAndPort;
        if (host.Length > 0 && host[0] == '[')
        {
            var close = host.IndexOf(']');
            if (close < 0 || close == 1)
            {
                throw new UriFormatException("Invalid IPv6 host.");
            }
            if (close + 1 < host.Length)
            {
                if (host[close + 1] != ':')
                {
                    throw new UriFormatException("Invalid authority.");
                }
                port = ports.Read(host.Substring(close + 2));
            }
            host = host.Substring(0, close + 1);
            if (host.IndexOf('[', 1) >= 0 || host.IndexOf(']', close + 1) >= 0)
            {
                throw new UriFormatException("Invalid IPv6 host.");
            }
        }
        else
        {
            var colon = hostAndPort.LastIndexOf(':');
            if (colon >= 0)
            {
                if (hostAndPort.IndexOf(':') != colon)
                {
                    throw new UriFormatException("Invalid IPv6 host.");
                }
                port = ports.Read(hostAndPort.Substring(colon + 1));
                host = hostAndPort.Substring(0, colon);
            }
        }

        return new UriAuthorityParts(userInfo, host, port);
    }

}
