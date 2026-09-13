// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

internal sealed class UriRelativeResolver(IUriSchemeEndReader schemes) : IUriRelativeResolver
{
    public string Resolve(Uri baseUri, string relative)
    {
        if (!baseUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("The base URI must be absolute.");
        }
        if (relative.Length == 0)
        {
            return baseUri.ToString();
        }
        if (schemes.Read(relative) >= 0)
        {
            return relative;
        }
        if (relative.StartsWith("//", StringComparison.Ordinal))
        {
            return baseUri.Scheme + ":" + relative;
        }

        var fragment = string.Empty;
        var withoutFragment = relative;
        var fragmentStart = relative.IndexOf('#');
        if (fragmentStart >= 0)
        {
            fragment = relative.Substring(fragmentStart);
            withoutFragment = relative.Substring(0, fragmentStart);
        }

        var query = string.Empty;
        var pathPart = withoutFragment;
        var queryStart = withoutFragment.IndexOf('?');
        if (queryStart >= 0)
        {
            query = withoutFragment.Substring(queryStart);
            pathPart = withoutFragment.Substring(0, queryStart);
        }
        if (pathPart.Length == 0)
        {
            return baseUri.Scheme + "://" + baseUri.Authority +
                baseUri.AbsolutePath +
                (query.Length == 0 ? baseUri.Query : query) + fragment;
        }

        var basePath = baseUri.AbsolutePath;
        var path = pathPart[0] == '/'
            ? pathPart
            : basePath.Substring(0, basePath.LastIndexOf('/') + 1) + pathPart;
        return baseUri.Scheme + "://" + baseUri.Authority + path + query + fragment;
    }
}
