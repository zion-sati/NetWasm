// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Net;
using System.Net.Cache;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace System.Xml
{
    // Resolves external XML resources named by a Uniform Resource Identifier (URI).
    public partial class XmlUrlResolver : XmlResolver
    {
        // NetWasm: credentials and proxy are retained configuration; file URIs, the only ones acquired, read neither.
#pragma warning disable CS0414
        private ICredentials? _credentials;
        private IWebProxy? _proxy;
#pragma warning restore CS0414

        public XmlUrlResolver() { }

        [UnsupportedOSPlatform("browser")]
        public override ICredentials? Credentials
        {
            set { _credentials = value; }
        }

        [UnsupportedOSPlatform("browser")]
        public IWebProxy? Proxy
        {
            set { _proxy = value; }
        }

        public RequestCachePolicy CachePolicy
        {
            set { } // nop, as caching isn't implemented
        }

        // Maps a URI to an Object containing the actual resource.
        // NetWasm: acquiring an entity needs host file or network access, which System.Xml does not have
        // (classified intentionally-unsupported); a caller-supplied XmlResolver can return the content instead.
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (ofObjectToReturn is null || ofObjectToReturn == typeof(Stream) || ofObjectToReturn == typeof(object))
            {
                return XmlDownloadManager.GetStream(absoluteUri, _credentials, _proxy);
            }

            throw new XmlException(SR.Xml_UnsupportedClass, string.Empty);
        }

        // Maps a URI to an Object containing the actual resource.
        public override async Task<object> GetEntityAsync(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (ofObjectToReturn == null || ofObjectToReturn == typeof(Stream) || ofObjectToReturn == typeof(object))
            {
                return await XmlDownloadManager.GetStreamAsync(absoluteUri, _credentials, _proxy).ConfigureAwait(false);
            }

            throw new XmlException(SR.Xml_UnsupportedClass, string.Empty);
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            base.ResolveUri(baseUri, relativeUri);
    }
}
